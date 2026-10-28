-- Pizza Magic! schema (plan section 4.3), run verbatim by the InitialSchema migration.
-- TS(col) and LTS(col) from the plan are expanded to their GLOB checks:
--   TS:  UTC instant  'YYYY-MM-DDTHH:MM:SS.fffZ' (fixed width, so text order = time order)
--   LTS: local value  'YYYY-MM-DDTHH:MM:SS' (no Z, no offset)

CREATE TABLE tax_class (
  id          INTEGER PRIMARY KEY,
  code        TEXT    NOT NULL UNIQUE,                  -- 'PREPARED_FOOD'
  name        TEXT    NOT NULL,
  sort_order  INTEGER NOT NULL DEFAULT 0,               -- display + tie-break
  is_active   INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);

CREATE TABLE tax_jurisdiction (
  id                      INTEGER PRIMARY KEY,
  code                    TEXT    NOT NULL UNIQUE,
  name                    TEXT    NOT NULL,
  kind                    TEXT    NOT NULL,             -- informational free text: 'State','County','City','Province',...
  parent_jurisdiction_id  INTEGER NULL REFERENCES tax_jurisdiction(id) ON DELETE RESTRICT,   -- NULL = outermost
  default_rate_ppm        INTEGER NOT NULL CHECK (default_rate_ppm BETWEEN 0 AND 1000000),
  -- tax rounding (§2.9): NULL = inherit from the nearest ancestor; the root (NULL parent) must set both
  -- (CatalogValidator; a CHECK can't see other rows). Seed WA: 'HALF_UP' + 'COMBINED' (RCW 82.08.054).
  tax_rounding_method     TEXT    NULL CHECK (tax_rounding_method IS NULL OR tax_rounding_method IN ('HALF_UP','TRUNCATE')),
  tax_rounding_scope      TEXT    NULL CHECK (tax_rounding_scope  IS NULL OR tax_rounding_scope  IN ('COMBINED','PER_LEVEL')),
  CHECK (parent_jurisdiction_id IS NULL OR parent_jurisdiction_id <> id)
);
CREATE INDEX ix_tax_jurisdiction_parent ON tax_jurisdiction(parent_jurisdiction_id);

CREATE TABLE jurisdiction_tax_class_rate (                -- no row => jurisdiction default applies
  tax_jurisdiction_id INTEGER NOT NULL REFERENCES tax_jurisdiction(id) ON DELETE CASCADE,
  tax_class_id        INTEGER NOT NULL REFERENCES tax_class(id)        ON DELETE RESTRICT,
  rate_ppm            INTEGER NOT NULL CHECK (rate_ppm BETWEEN 0 AND 1000000),   -- 0 = explicit exemption
  PRIMARY KEY (tax_jurisdiction_id, tax_class_id)
);

CREATE TABLE time_zone (                                  -- UQ15; rows come from the GENERATED data/seed/0000-time-zones.sql
  id            INTEGER PRIMARY KEY,
  iana_id       TEXT    NOT NULL UNIQUE,                  -- 'America/Los_Angeles' (a name only; no runtime tz lookup)
  display_name  TEXT    NOT NULL,                         -- 'Pacific Time (Los Angeles)'
  is_active     INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);

CREATE TABLE time_zone_offset_period (                    -- half-open [valid_from_utc, valid_to_utc)
  id                  INTEGER PRIMARY KEY,
  time_zone_id        INTEGER NOT NULL REFERENCES time_zone(id) ON DELETE CASCADE,
  valid_from_utc      TEXT    NOT NULL CHECK (valid_from_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  valid_to_utc        TEXT    NOT NULL CHECK (valid_to_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  utc_offset_minutes  INTEGER NOT NULL CHECK (utc_offset_minutes BETWEEN -840 AND 840),
  abbreviation        TEXT    NOT NULL,                   -- 'PDT' / 'PST'
  is_dst              INTEGER NOT NULL CHECK (is_dst IN (0,1)),
  UNIQUE (time_zone_id, valid_from_utc),
  CHECK (valid_from_utc < valid_to_utc)                   -- fixed-width text: lexical order = time order
);   -- contiguity (each to = next from), no overlap and coverage are cross-row: CatalogValidator (TZ8-TZ10)
CREATE INDEX ix_tzop_lookup ON time_zone_offset_period(time_zone_id, valid_from_utc, valid_to_utc);

CREATE TABLE restaurant (
  id                   INTEGER PRIMARY KEY,
  code                 TEXT    NOT NULL UNIQUE,
  name                 TEXT    NOT NULL,
  time_zone_id         INTEGER NOT NULL REFERENCES time_zone(id) ON DELETE RESTRICT,   -- UQ15 FK; evaluates *_local rules
  tax_jurisdiction_id  INTEGER NOT NULL REFERENCES tax_jurisdiction(id) ON DELETE RESTRICT,
  currency_code        TEXT    NOT NULL DEFAULT 'USD' CHECK (currency_code = 'USD'),
  is_default           INTEGER NOT NULL DEFAULT 0 CHECK (is_default IN (0,1)),
  is_active            INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);
CREATE UNIQUE INDEX ux_restaurant_one_default ON restaurant(is_default) WHERE is_default = 1;

CREATE TABLE ordering_policy (                            -- menu-facing limits (A57); one row per restaurant
  restaurant_id             INTEGER PRIMARY KEY REFERENCES restaurant(id) ON DELETE CASCADE,
  max_lines                 INTEGER NOT NULL CHECK (max_lines >= 1),
  max_line_quantity         INTEGER NOT NULL CHECK (max_line_quantity >= 1),
  max_toppings_per_pizza    INTEGER NOT NULL CHECK (max_toppings_per_pizza >= 0),   -- distinct toppings (§2.5 step 2)
  max_promo_codes           INTEGER NOT NULL CHECK (max_promo_codes >= 1),
  max_tip_amount_cents      INTEGER NOT NULL CHECK (max_tip_amount_cents >= 0),
  max_tip_percent_ppm       INTEGER NOT NULL CHECK (max_tip_percent_ppm BETWEEN 0 AND 1000000),
  tip_presets_ppm           TEXT    NOT NULL DEFAULT '[]' CHECK (json_valid(tip_presets_ppm))
);

CREATE TABLE order_channel (
  code          TEXT    PRIMARY KEY CHECK (code NOT GLOB '*[^A-Z_]*' AND length(code) BETWEEN 1 AND 20),  -- 'ONLINE'
  display_name  TEXT    NOT NULL,
  sort_order    INTEGER NOT NULL DEFAULT 0,
  is_active     INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);
CREATE TABLE product_type (                               -- mirrors a C# enum; structural (A50)
  code  TEXT PRIMARY KEY CHECK (code IN ('PIZZA_SIZE','TOPPING','CRUST','SAUCE','ADDON')),
  name  TEXT NOT NULL
);

CREATE TABLE product_category (                           -- 'SIDES','DRINKS','DESSERTS'
  id                 INTEGER PRIMARY KEY,
  code               TEXT    NOT NULL UNIQUE,
  name               TEXT    NOT NULL,
  product_type_code  TEXT    NOT NULL REFERENCES product_type(code),
  sort_order         INTEGER NOT NULL DEFAULT 0,
  is_active          INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))   -- 0 => its products unavailable (A38)
);

CREATE TABLE topping_tier (                               -- the spec's "topping category"
  id              INTEGER PRIMARY KEY,
  code            TEXT    NOT NULL UNIQUE,                -- 'BASIC','PREMIUM'
  display_name    TEXT    NOT NULL,
  pricing_method  TEXT    NOT NULL DEFAULT 'percent_of_base',   -- registry key (validator checks params)
  rate_ppm        INTEGER NULL CHECK (rate_ppm IS NULL OR rate_ppm BETWEEN 0 AND 1000000),
  amount_cents    INTEGER NULL CHECK (amount_cents IS NULL OR amount_cents >= 0),
  sort_order      INTEGER NOT NULL DEFAULT 0,
  is_active       INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);

CREATE TABLE product (
  id                 INTEGER PRIMARY KEY,
  product_type_code  TEXT    NOT NULL REFERENCES product_type(code),
  code               TEXT    NOT NULL UNIQUE CHECK (code NOT GLOB '*[^A-Z0-9_]*' AND length(code) BETWEEN 1 AND 40),
  display_name       TEXT    NOT NULL,
  category_id        INTEGER NULL REFERENCES product_category(id) ON DELETE RESTRICT,
  topping_tier_id    INTEGER NULL REFERENCES topping_tier(id)     ON DELETE RESTRICT,
  price_cents        INTEGER NULL CHECK (price_cents IS NULL OR price_cents >= 0),  -- size base / crust / sauce / add-on
  pricing_method     TEXT    NULL,     -- TOPPING only: optional GLOBAL per-topping rule (NULL => tier rule)
  rate_ppm           INTEGER NULL CHECK (rate_ppm IS NULL OR rate_ppm BETWEEN 0 AND 1000000),
  amount_cents       INTEGER NULL CHECK (amount_cents IS NULL OR amount_cents >= 0),
  max_multiplier     INTEGER NULL CHECK (max_multiplier IS NULL OR max_multiplier BETWEEN 1 AND 10),
  tax_class_id       INTEGER NOT NULL REFERENCES tax_class(id) ON DELETE RESTRICT,   -- every sellable (A51)
  is_default         INTEGER NOT NULL DEFAULT 0 CHECK (is_default IN (0,1)),
  sort_order         INTEGER NOT NULL DEFAULT 0,
  is_active          INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),            -- removal = deactivate
  CHECK ( (product_type_code = 'TOPPING'
             AND topping_tier_id IS NOT NULL AND price_cents IS NULL AND max_multiplier IS NOT NULL)
       OR (product_type_code <> 'TOPPING'
             AND topping_tier_id IS NULL AND price_cents IS NOT NULL AND max_multiplier IS NULL
             AND pricing_method IS NULL AND rate_ppm IS NULL AND amount_cents IS NULL) ),
  CHECK (product_type_code <> 'PIZZA_SIZE' OR price_cents > 0),
  CHECK (is_default = 0 OR product_type_code IN ('CRUST','SAUCE')),
  CHECK (category_id IS NULL OR product_type_code = 'ADDON')
);
CREATE UNIQUE INDEX ux_product_one_default ON product(product_type_code) WHERE is_default = 1;
CREATE INDEX ix_product_tier ON product(topping_tier_id);

CREATE TABLE portion_type (                               -- coverage price factor (decision I)
  code              TEXT    PRIMARY KEY,                  -- 'WHOLE','HALF','QUARTER'
  name              TEXT    NOT NULL,
  price_factor_ppm  INTEGER NOT NULL CHECK (price_factor_ppm BETWEEN 0 AND 1000000)
);

CREATE TABLE portion (                                    -- which half / quarter
  code               TEXT    PRIMARY KEY,                 -- 'WHOLE','LEFT','RIGHT','Q1'..'Q4'
  portion_type_code  TEXT    NOT NULL REFERENCES portion_type(code),
  coverage_mask      INTEGER NOT NULL UNIQUE CHECK (coverage_mask BETWEEN 1 AND 15),
  label              TEXT    NOT NULL,                    -- 'left half', 'upper-right quarter'
  sort_order         INTEGER NOT NULL                     -- display order; ties broken by coverage_mask (UNIQUE), §2.5
);

-- Sparse per-restaurant overrides: no row => global value. Default restaurant has none.
CREATE TABLE restaurant_product_override (
  restaurant_id   INTEGER NOT NULL REFERENCES restaurant(id) ON DELETE CASCADE,
  product_id      INTEGER NOT NULL REFERENCES product(id)    ON DELETE RESTRICT,
  is_available    INTEGER NULL CHECK (is_available IS NULL OR is_available IN (0,1)),   -- NULL = inherit
  price_cents     INTEGER NULL CHECK (price_cents IS NULL OR price_cents >= 0),         -- non-topping products
  pricing_method  TEXT    NULL,                                                          -- toppings: rule triple
  rate_ppm        INTEGER NULL CHECK (rate_ppm IS NULL OR rate_ppm BETWEEN 0 AND 1000000),
  amount_cents    INTEGER NULL CHECK (amount_cents IS NULL OR amount_cents >= 0),
  PRIMARY KEY (restaurant_id, product_id),
  CHECK (price_cents IS NULL OR pricing_method IS NULL)
);   -- type-specific rules (price only for non-toppings, size price > 0) need a join: CatalogValidator

CREATE TABLE restaurant_topping_tier_override (
  restaurant_id    INTEGER NOT NULL REFERENCES restaurant(id)   ON DELETE CASCADE,
  topping_tier_id  INTEGER NOT NULL REFERENCES topping_tier(id) ON DELETE RESTRICT,
  pricing_method   TEXT    NOT NULL,
  rate_ppm         INTEGER NULL CHECK (rate_ppm IS NULL OR rate_ppm BETWEEN 0 AND 1000000),
  amount_cents     INTEGER NULL CHECK (amount_cents IS NULL OR amount_cents >= 0),
  PRIMARY KEY (restaurant_id, topping_tier_id)
);

CREATE TABLE restaurant_portion_type_override (           -- per-store coverage factor (decision I)
  restaurant_id      INTEGER NOT NULL REFERENCES restaurant(id) ON DELETE CASCADE,
  portion_type_code  TEXT    NOT NULL REFERENCES portion_type(code),
  price_factor_ppm   INTEGER NOT NULL CHECK (price_factor_ppm BETWEEN 0 AND 1000000),
  PRIMARY KEY (restaurant_id, portion_type_code)
);
CREATE TABLE promotion (
  id                    INTEGER PRIMARY KEY,
  name                  TEXT    NOT NULL,
  customer_description  TEXT    NOT NULL,               -- '10% off your order before tax'
  effect_type           TEXT    NOT NULL,               -- registry key; phase comes from the registered class
  effect_params         TEXT    NOT NULL CHECK (json_valid(effect_params)),   -- integer *Cents / *Ppm keys (A67)
  priority              INTEGER NOT NULL DEFAULT 100,   -- order within a phase (lower first)
  is_exclusive          INTEGER NOT NULL DEFAULT 0 CHECK (is_exclusive IN (0,1)),
  exclusion_group       TEXT    NULL,
  -- UQ16: local wall-clock 'YYYY-MM-DDTHH:MM:SS' (no Z/offset), evaluated in EACH restaurant's zone (§2.6.1)
  valid_from_local      TEXT    NULL CHECK (valid_from_local IS NULL OR valid_from_local GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]'),   -- inclusive
  valid_to_local        TEXT    NULL CHECK (valid_to_local   IS NULL OR valid_to_local GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9]'),     -- exclusive
  is_active             INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1)),
  CHECK (valid_from_local IS NULL OR valid_to_local IS NULL OR valid_from_local < valid_to_local)
  -- [STRETCH] max_redemptions INTEGER NULL, redemption_count INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE promotion_code (                            -- many codes -> one promotion
  code          TEXT    PRIMARY KEY CHECK (length(code) BETWEEN 1 AND 32 AND code NOT GLOB '*[^A-Z0-9-]*'),
  promotion_id  INTEGER NOT NULL REFERENCES promotion(id) ON DELETE RESTRICT,
  is_active     INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0,1))
);
CREATE INDEX ix_promotion_code_promotion ON promotion_code(promotion_id);

CREATE TABLE promotion_condition (                       -- all rows AND-ed
  id               INTEGER PRIMARY KEY,
  promotion_id     INTEGER NOT NULL REFERENCES promotion(id) ON DELETE CASCADE,
  condition_type   TEXT    NOT NULL,                     -- registry key
  params           TEXT    NOT NULL CHECK (json_valid(params)),
  failure_message  TEXT    NULL,
  sort_order       INTEGER NOT NULL DEFAULT 0
);
CREATE INDEX ix_promotion_condition_promotion ON promotion_condition(promotion_id);

CREATE TABLE promotion_incompatibility (                 -- symmetric, stored once (low < high)
  promotion_id_low   INTEGER NOT NULL REFERENCES promotion(id) ON DELETE CASCADE,
  promotion_id_high  INTEGER NOT NULL REFERENCES promotion(id) ON DELETE CASCADE,
  PRIMARY KEY (promotion_id_low, promotion_id_high),
  CHECK (promotion_id_low < promotion_id_high)
);
CREATE INDEX ix_promotion_incompat_high ON promotion_incompatibility(promotion_id_high);

CREATE TABLE promotion_restaurant (                      -- no rows for a promotion => all restaurants
  promotion_id   INTEGER NOT NULL REFERENCES promotion(id)  ON DELETE CASCADE,
  restaurant_id  INTEGER NOT NULL REFERENCES restaurant(id) ON DELETE CASCADE,
  PRIMARY KEY (promotion_id, restaurant_id)
);

CREATE TABLE promotion_target (                          -- [EXT] item scope; none => order-scoped
  id                 INTEGER PRIMARY KEY,
  promotion_id       INTEGER NOT NULL REFERENCES promotion(id) ON DELETE CASCADE,
  role               TEXT    NOT NULL CHECK (role IN ('DISCOUNT','BUY','GET')),
  target_kind        TEXT    NOT NULL CHECK (target_kind IN ('PRODUCT_TYPE','PRODUCT','CATEGORY','TOPPING_TIER')),
  product_type_code  TEXT    NULL REFERENCES product_type(code),
  product_id         INTEGER NULL REFERENCES product(id)          ON DELETE RESTRICT,
  category_id        INTEGER NULL REFERENCES product_category(id) ON DELETE RESTRICT,
  topping_tier_id    INTEGER NULL REFERENCES topping_tier(id)     ON DELETE RESTRICT,
  is_exclusion       INTEGER NOT NULL DEFAULT 0 CHECK (is_exclusion IN (0,1)),
  CHECK ( (target_kind='PRODUCT_TYPE' AND product_type_code IS NOT NULL AND product_id IS NULL AND category_id IS NULL AND topping_tier_id IS NULL)
       OR (target_kind='PRODUCT'      AND product_id IS NOT NULL AND product_type_code IS NULL AND category_id IS NULL AND topping_tier_id IS NULL)
       OR (target_kind='CATEGORY'     AND category_id IS NOT NULL AND product_type_code IS NULL AND product_id IS NULL AND topping_tier_id IS NULL)
       OR (target_kind='TOPPING_TIER' AND topping_tier_id IS NOT NULL AND product_type_code IS NULL AND product_id IS NULL AND category_id IS NULL) )
);   -- target kinds have typed FK columns, so a NEW target kind is a migration + matcher class (rare; documented)
CREATE INDEX ix_promotion_target_promotion ON promotion_target(promotion_id);
CREATE TABLE order_status (
  code          TEXT    PRIMARY KEY,   -- 'NotStarted','Started','ReadyForPickup','Delivered','Cancelled'
  display_name  TEXT    NOT NULL,
  sort_order    INTEGER NOT NULL,
  is_terminal   INTEGER NOT NULL DEFAULT 0 CHECK (is_terminal IN (0,1))
);

CREATE TABLE customer_order (
  id                     TEXT    PRIMARY KEY,                       -- GUID (unguessable)
  restaurant_id          INTEGER NOT NULL REFERENCES restaurant(id) ON DELETE RESTRICT,
  channel_code           TEXT    NOT NULL REFERENCES order_channel(code),
  status                 TEXT    NOT NULL DEFAULT 'NotStarted' REFERENCES order_status(code),
  version                INTEGER NOT NULL DEFAULT 1 CHECK (version >= 1),   -- ETag; incremented explicitly
  customer_name          TEXT    NULL CHECK (customer_name IS NULL OR length(customer_name) BETWEEN 1 AND 50),
  -- pricing context snapshot
  tax_jurisdiction_id    INTEGER NOT NULL REFERENCES tax_jurisdiction(id),
  time_zone_id           INTEGER NOT NULL REFERENCES time_zone(id) ON DELETE RESTRICT,   -- zone snapshot (UQ15 FK)
  priced_at_utc          TEXT    NOT NULL CHECK (priced_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  -- money snapshot
  subtotal_cents         INTEGER NOT NULL CHECK (subtotal_cents >= 0),
  discount_cents         INTEGER NOT NULL DEFAULT 0,
  taxable_cents          INTEGER NOT NULL,
  tax_cents              INTEGER NOT NULL CHECK (tax_cents >= 0),
  tax_rounding_method    TEXT    NOT NULL CHECK (tax_rounding_method IN ('HALF_UP','TRUNCATE')),   -- resolved settings,
  tax_rounding_scope     TEXT    NOT NULL CHECK (tax_rounding_scope  IN ('COMBINED','PER_LEVEL')),  -- snapshotted (§2.9)
  tip_mode               TEXT    NOT NULL DEFAULT 'None' CHECK (tip_mode IN ('None','Amount','Percent')),
  tip_input              TEXT    NULL,                              -- exact text entered: '4.50' / '17.5'
  tip_rate_ppm           INTEGER NULL CHECK (tip_rate_ppm IS NULL OR tip_rate_ppm BETWEEN 0 AND 1000000),
  tip_cents              INTEGER NOT NULL DEFAULT 0 CHECK (tip_cents >= 0),
  total_cents            INTEGER NOT NULL,
  -- timestamps
  created_at_utc         TEXT    NOT NULL CHECK (created_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  updated_at_utc         TEXT    NOT NULL CHECK (updated_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  status_changed_at_utc  TEXT    NOT NULL CHECK (status_changed_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  CONSTRAINT ck_order_discount CHECK (discount_cents BETWEEN 0 AND subtotal_cents),
  CONSTRAINT ck_order_taxable  CHECK (taxable_cents = subtotal_cents - discount_cents),
  CONSTRAINT ck_order_total    CHECK (total_cents = taxable_cents + tax_cents + tip_cents),
  CONSTRAINT ck_order_tip_mode CHECK (
       (tip_mode = 'None'    AND tip_input IS NULL     AND tip_rate_ppm IS NULL     AND tip_cents = 0)
    OR (tip_mode = 'Amount'  AND tip_input IS NOT NULL AND tip_rate_ppm IS NULL)
    OR (tip_mode = 'Percent' AND tip_input IS NOT NULL AND tip_rate_ppm IS NOT NULL))
);
CREATE INDEX ix_customer_order_status_changed ON customer_order(status, status_changed_at_utc);  -- simulator / kitchen queue

CREATE TABLE order_line (
  id                   INTEGER PRIMARY KEY,
  order_id             TEXT    NOT NULL REFERENCES customer_order(id) ON DELETE CASCADE,
  line_number          INTEGER NOT NULL CHECK (line_number >= 1),       -- customer order; tie-break ordinal
  line_kind            TEXT    NOT NULL CHECK (line_kind IN ('PIZZA','ADDON')),
  product_id           INTEGER NOT NULL REFERENCES product(id) ON DELETE RESTRICT,   -- size (PIZZA) / add-on (ADDON)
  product_code         TEXT    NOT NULL,                                -- snapshots ↓
  product_name         TEXT    NOT NULL,
  base_price_cents     INTEGER NOT NULL CHECK (base_price_cents >= 0),  -- effective size base / add-on unit price
  base_price_source    TEXT    NOT NULL CHECK (base_price_source IN ('Global','Restaurant')),
  tax_class_id         INTEGER NOT NULL REFERENCES tax_class(id),
  crust_product_id     INTEGER NULL REFERENCES product(id) ON DELETE RESTRICT,
  crust_code           TEXT    NULL,
  crust_name           TEXT    NULL,
  crust_is_default     INTEGER NULL CHECK (crust_is_default IS NULL OR crust_is_default IN (0,1)),  -- description (A40)
  crust_price_cents    INTEGER NULL CHECK (crust_price_cents IS NULL OR crust_price_cents >= 0),
  crust_tax_class_id   INTEGER NULL REFERENCES tax_class(id),
  sauce_product_id     INTEGER NULL REFERENCES product(id) ON DELETE RESTRICT,
  sauce_code           TEXT    NULL,
  sauce_name           TEXT    NULL,
  sauce_is_default     INTEGER NULL CHECK (sauce_is_default IS NULL OR sauce_is_default IN (0,1)),  -- description (A40)
  sauce_price_cents    INTEGER NULL CHECK (sauce_price_cents IS NULL OR sauce_price_cents >= 0),
  sauce_tax_class_id   INTEGER NULL REFERENCES tax_class(id),
  quantity             INTEGER NOT NULL CHECK (quantity >= 1),
  unit_price_cents     INTEGER NOT NULL,                                -- U (exact sum of rounded parts)
  line_total_cents     INTEGER NOT NULL,
  discount_cents       INTEGER NOT NULL DEFAULT 0,                      -- Σ allocations to this line (all promos)
  UNIQUE (order_id, line_number),
  CHECK ( (line_kind = 'PIZZA' AND crust_product_id IS NOT NULL AND crust_price_cents IS NOT NULL AND crust_tax_class_id IS NOT NULL
                               AND crust_is_default IS NOT NULL AND crust_code IS NOT NULL AND crust_name IS NOT NULL
                               AND sauce_product_id IS NOT NULL AND sauce_price_cents IS NOT NULL AND sauce_tax_class_id IS NOT NULL
                               AND sauce_is_default IS NOT NULL AND sauce_code IS NOT NULL AND sauce_name IS NOT NULL)
       OR (line_kind = 'ADDON' AND crust_product_id IS NULL AND sauce_product_id IS NULL
                               AND crust_is_default IS NULL AND sauce_is_default IS NULL
                               AND unit_price_cents = base_price_cents) ),
  CHECK (unit_price_cents >= base_price_cents),
  CHECK (line_total_cents = unit_price_cents * quantity),
  CHECK (discount_cents BETWEEN 0 AND line_total_cents)
);

CREATE TABLE order_line_topping (                        -- one row per BILLED placement (canonical cover, §2.5)
  id                           INTEGER PRIMARY KEY,
  order_line_id                INTEGER NOT NULL REFERENCES order_line(id) ON DELETE CASCADE,
  sort_order                   INTEGER NOT NULL,          -- canonical display order (portion, topping)
  topping_product_id           INTEGER NOT NULL REFERENCES product(id) ON DELETE RESTRICT,
  topping_code                 TEXT    NOT NULL,
  topping_name                 TEXT    NOT NULL,
  tier_code                    TEXT    NOT NULL,
  portion_code                 TEXT    NOT NULL REFERENCES portion(code),
  portion_label                TEXT    NOT NULL,          -- snapshot: 'left half' (description never reads live catalog)
  coverage_mask                INTEGER NOT NULL CHECK (coverage_mask BETWEEN 1 AND 15),   -- snapshot of portion.coverage_mask
  coverage_phrase              TEXT    NOT NULL,          -- snapshot: the topping's coverage phrase (§2.5), e.g.
                                                          --   'Pepperoni (whole) + extra (left half)'; same on every row of
                                                          --   this (line, topping); the description renders it once
  portion_type_code            TEXT    NOT NULL REFERENCES portion_type(code),
  multiplier                   INTEGER NOT NULL CHECK (multiplier >= 1),
  price_source                 TEXT    NOT NULL CHECK (price_source IN ('RestaurantTopping','RestaurantTier','GlobalTopping','GlobalTier')),
  pricing_method               TEXT    NOT NULL,
  rate_ppm                     INTEGER NULL,
  topping_unit_price_cents     INTEGER NOT NULL CHECK (topping_unit_price_cents >= 0),     -- RP1
  coverage_factor_ppm          INTEGER NOT NULL CHECK (coverage_factor_ppm BETWEEN 0 AND 1000000),
  coverage_factor_source       TEXT    NOT NULL CHECK (coverage_factor_source IN ('Global','Restaurant')),
  placement_unit_charge_cents  INTEGER NOT NULL CHECK (placement_unit_charge_cents >= 0),  -- RP2
  placement_charge_cents       INTEGER NOT NULL,                                           -- per pizza
  tax_class_id                 INTEGER NOT NULL REFERENCES tax_class(id),
  UNIQUE (order_line_id, topping_product_id, portion_code),   -- identical billed portions are merged into a multiplier
  UNIQUE (order_line_id, sort_order),
  UNIQUE (id, order_line_id),                                 -- target of the composite FK below
  CHECK (placement_charge_cents = placement_unit_charge_cents * multiplier)
);   -- "billed rows of a topping = cheapest exact cover of their own coverage counts, each count <= max_multiplier"
     -- is a domain rule (SQL can't express it without triggers); verified by the §4.4 reload test
CREATE INDEX ix_olt_topping ON order_line_topping(topping_product_id);

CREATE TABLE order_promotion (                           -- applied codes only (A12)
  id                   INTEGER PRIMARY KEY,
  order_id             TEXT    NOT NULL REFERENCES customer_order(id) ON DELETE CASCADE,
  sequence             INTEGER NOT NULL,                 -- entry order
  code                 TEXT    NOT NULL,
  promotion_id         INTEGER NOT NULL REFERENCES promotion(id) ON DELETE RESTRICT,
  effect_type          TEXT    NOT NULL,
  phase                TEXT    NOT NULL CHECK (phase IN ('Item','Order')),
  discount_cents       INTEGER NOT NULL CHECK (discount_cents > 0),
  message              TEXT    NOT NULL,
  definition_snapshot  TEXT    NOT NULL CHECK (json_valid(definition_snapshot)),   -- effect+conditions+targets+priority+conflicts
  UNIQUE (order_id, sequence)
);

CREATE TABLE order_discount_allocation (                 -- where each discount cent landed (aggregated over units)
  id                     INTEGER PRIMARY KEY,
  order_promotion_id     INTEGER NOT NULL REFERENCES order_promotion(id)    ON DELETE CASCADE,
  order_line_id          INTEGER NOT NULL REFERENCES order_line(id)         ON DELETE CASCADE,
  component_kind         TEXT    NOT NULL CHECK (component_kind IN ('BASE','CRUST','SAUCE','TOPPING')),
  order_line_topping_id  INTEGER NULL,
  tax_class_id           INTEGER NOT NULL REFERENCES tax_class(id),
  amount_cents           INTEGER NOT NULL CHECK (amount_cents > 0),            -- zero shares are not stored
  CHECK ((component_kind = 'TOPPING') = (order_line_topping_id IS NOT NULL)),
  -- the placement must belong to the same line (not enforced when NULL, i.e. non-topping components)
  FOREIGN KEY (order_line_topping_id, order_line_id)
    REFERENCES order_line_topping(id, order_line_id) ON DELETE CASCADE
);
-- one row per aggregated grain; two partial indexes because SQLite treats NULLs in UNIQUE as distinct
CREATE UNIQUE INDEX ux_oda_component ON order_discount_allocation(order_promotion_id, order_line_id, component_kind)
  WHERE order_line_topping_id IS NULL;
CREATE UNIQUE INDEX ux_oda_topping   ON order_discount_allocation(order_promotion_id, order_line_topping_id)
  WHERE order_line_topping_id IS NOT NULL;
CREATE INDEX ix_oda_line ON order_discount_allocation(order_line_id);

CREATE TABLE order_tax_line (
  id                   INTEGER PRIMARY KEY,
  order_id             TEXT    NOT NULL REFERENCES customer_order(id) ON DELETE CASCADE,
  tax_jurisdiction_id  INTEGER NOT NULL REFERENCES tax_jurisdiction(id),
  jurisdiction_code    TEXT    NOT NULL,
  jurisdiction_name    TEXT    NOT NULL,
  jurisdiction_depth   INTEGER NOT NULL CHECK (jurisdiction_depth >= 0),  -- 0 = outermost
  tax_class_id         INTEGER NOT NULL REFERENCES tax_class(id),
  tax_class_code       TEXT    NOT NULL,
  gross_cents          INTEGER NOT NULL CHECK (gross_cents > 0),          -- class bucket before discounts
  discount_cents       INTEGER NOT NULL CHECK (discount_cents >= 0),
  taxable_base_cents   INTEGER NOT NULL CHECK (taxable_base_cents >= 0),
  rate_ppm             INTEGER NOT NULL CHECK (rate_ppm BETWEEN 0 AND 1000000),
  rate_source          TEXT    NOT NULL CHECK (rate_source IN ('Default','Override')),
  tax_cents            INTEGER NOT NULL CHECK (tax_cents >= 0),
  UNIQUE (order_id, tax_jurisdiction_id, tax_class_id),
  CHECK (taxable_base_cents = gross_cents - discount_cents)
);

CREATE TABLE order_status_history (
  id               INTEGER PRIMARY KEY,
  order_id         TEXT    NOT NULL REFERENCES customer_order(id) ON DELETE CASCADE,
  from_status      TEXT    NULL REFERENCES order_status(code),     -- NULL on creation
  to_status        TEXT    NOT NULL REFERENCES order_status(code),
  event_type       TEXT    NOT NULL CHECK (event_type IN ('Created','Updated','StatusChanged','Cancelled')),
  actor            TEXT    NOT NULL CHECK (actor IN ('Customer','Kitchen','Simulator','System')),
  order_version    INTEGER NOT NULL,                                -- version AFTER this event
  occurred_at_utc  TEXT    NOT NULL CHECK (occurred_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z')
);
CREATE INDEX ix_osh_order ON order_status_history(order_id, id);

CREATE TABLE idempotency_record (
  idempotency_key  TEXT    NOT NULL CHECK (length(idempotency_key) BETWEEN 1 AND 64),
  operation        TEXT    NOT NULL CHECK (operation IN ('CreateOrder','UpdateOrder')),
  request_hash     TEXT    NOT NULL CHECK (length(request_hash) = 64),   -- SHA-256 hex of the normalized raw body
  response_status  INTEGER NOT NULL,
  response_body    TEXT    NOT NULL,
  response_etag    TEXT    NULL,
  order_id         TEXT    NOT NULL REFERENCES customer_order(id) ON DELETE CASCADE,
  created_at_utc   TEXT    NOT NULL CHECK (created_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  expires_at_utc   TEXT    NOT NULL CHECK (expires_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z'),
  PRIMARY KEY (idempotency_key, operation)
);   -- expired row for the same (key, operation) is DELETEd inside the writing txn before INSERT (§2.11)
CREATE INDEX ix_idem_expires ON idempotency_record(expires_at_utc);   -- purge job [STRETCH]

CREATE TABLE data_script_log (                           -- §2.12 (same name + same hash => no-op; different hash => refused)
  name            TEXT PRIMARY KEY,                      -- 'seed/0001-core.sql', 'examples/tuesday20.sql'
  sha256          TEXT NOT NULL CHECK (length(sha256) = 64),
  applied_at_utc  TEXT NOT NULL CHECK (applied_at_utc GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z')
);

-- [STRETCH] order_revision(order_id, version, snapshot_json, created_at_utc)  — pre-edit audit
