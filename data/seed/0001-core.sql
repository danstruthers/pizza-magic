-- Core seed (plan section 4.5): the spec catalog, Washington tax, the default restaurant, policy, channels,
-- statuses and the ILOVEPIZZA / SAVE5 promotions. Applied once, after 0000-time-zones.sql (section 2.12).

INSERT INTO tax_class (id, code, name, sort_order) VALUES
 (1,'PREPARED_FOOD','Prepared food',10), (2,'BEVERAGE','Beverage',20),
 (3,'GENERAL','General merchandise',30);                          -- GENERAL unused: "new class = data"
INSERT INTO tax_jurisdiction (id, code, name, kind, parent_jurisdiction_id, default_rate_ppm,
                              tax_rounding_method, tax_rounding_scope) VALUES
 (1,'WA','Washington State','State',NULL,65000,'HALF_UP','COMBINED');
  -- 6.5% = WA state sales tax rate; no class-rate rows. Rounding per RCW 82.08.054 / SSUTA §324:
  -- aggregated state + local tax, third decimal > 4 rounds up (half away from zero), invoice basis.
INSERT INTO restaurant (id, code, name, time_zone_id, tax_jurisdiction_id, is_default) VALUES
 (1,'MAIN','Pizza Magic',1,1,1);   -- time_zone 1 = America/Los_Angeles (from 0000-time-zones.sql; UQ13, UQ15)
INSERT INTO ordering_policy VALUES (1, 20, 20, 12, 3, 50000, 1000000, '[150000,180000,200000]');
INSERT INTO order_channel (code, display_name, sort_order) VALUES
 ('ONLINE','Online',1), ('IN_STORE','In store',2), ('PHONE','Phone',3);

INSERT INTO product_type (code, name) VALUES ('PIZZA_SIZE','Pizza size'), ('TOPPING','Topping'),
 ('CRUST','Crust'), ('SAUCE','Sauce'), ('ADDON','Add-on');
INSERT INTO topping_tier (id, code, display_name, pricing_method, rate_ppm, sort_order) VALUES
 (1,'BASIC','Basic','percent_of_base', 80000,1),
 (2,'PREMIUM','Premium','percent_of_base',110000,2);
INSERT INTO product (id, product_type_code, code, display_name, price_cents, tax_class_id, sort_order) VALUES
 (1,'PIZZA_SIZE','SMALL','Small',500,1,1), (2,'PIZZA_SIZE','MEDIUM','Medium',700,1,2),
 (3,'PIZZA_SIZE','LARGE','Large',1200,1,3);
INSERT INTO product (id, product_type_code, code, display_name, topping_tier_id, max_multiplier, tax_class_id, sort_order) VALUES
 (101,'TOPPING','PEPPERONI','Pepperoni',1,3,1,1),         (102,'TOPPING','SAUSAGE','Sausage',1,3,1,2),
 (103,'TOPPING','EXTRA_CHEESE','Extra cheese',1,3,1,3),   (104,'TOPPING','ONIONS','Onions',1,3,1,4),
 (105,'TOPPING','BLACK_OLIVES','Black olives',1,3,1,5),   (106,'TOPPING','GREEN_PEPPERS','Green peppers',1,3,1,6),
 (107,'TOPPING','BACON','Bacon',2,3,1,7),                 (108,'TOPPING','CHICKEN','Chicken',2,3,1,8),
 (109,'TOPPING','GARLIC','Garlic',2,3,1,9),               (110,'TOPPING','ARTICHOKE_HEARTS','Artichoke hearts',2,3,1,10),
 (111,'TOPPING','SUN_DRIED_TOMATOES','Sun-dried tomatoes',2,3,1,11), (112,'TOPPING','PINEAPPLE','Pineapple',2,3,1,12);
INSERT INTO product (id, product_type_code, code, display_name, price_cents, tax_class_id, is_default, sort_order) VALUES
 (201,'CRUST','HAND_TOSSED','Hand tossed',0,1,1,1),
 (301,'SAUCE','CLASSIC_TOMATO','Classic tomato',0,1,1,1);

INSERT INTO portion_type (code, name, price_factor_ppm) VALUES
 ('WHOLE','Whole',1000000), ('HALF','Half',600000), ('QUARTER','Quarter',400000);
INSERT INTO portion (code, portion_type_code, coverage_mask, label, sort_order) VALUES
 ('WHOLE','WHOLE',15,'whole',1), ('LEFT','HALF',6,'left half',2), ('RIGHT','HALF',9,'right half',3),
 ('Q1','QUARTER',1,'upper-right quarter',4), ('Q2','QUARTER',2,'upper-left quarter',5),
 ('Q3','QUARTER',4,'lower-left quarter',6),  ('Q4','QUARTER',8,'lower-right quarter',7);

INSERT INTO promotion (id, name, customer_description, effect_type, effect_params, priority,
                       valid_from_local, valid_to_local) VALUES       -- NULL/NULL = no validity window (UQ16)
 (1,'I Love Pizza','10% off your order before tax','percent_off_order','{"ratePpm":100000,"maxDiscountCents":null}',100,NULL,NULL),
 (2,'Save Five','$5 off orders over $20 before tax','fixed_off_order','{"amountCents":500}',100,NULL,NULL);
INSERT INTO promotion_code (code, promotion_id) VALUES ('ILOVEPIZZA',1), ('SAVE5',2);
INSERT INTO promotion_condition (id, promotion_id, condition_type, params) VALUES
 (1,2,'min_subtotal','{"amountCents":2000,"inclusive":false}');   -- "over $20" = strictly greater
INSERT INTO promotion_incompatibility (promotion_id_low, promotion_id_high) VALUES (1,2);

INSERT INTO order_status (code, display_name, sort_order, is_terminal) VALUES
 ('NotStarted','Not Started',1,0), ('Started','Started',2,0), ('ReadyForPickup','Ready for Pickup',3,0),
 ('Delivered','Delivered',4,1), ('Cancelled','Cancelled',5,1);
-- No restaurant_*_override rows: every spec price comes straight from the global catalog.
