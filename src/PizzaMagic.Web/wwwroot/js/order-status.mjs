// Order status polling (plan section 6.1): every 5 s until the order reaches a final status. M14 red stub.

export const FINAL_STATUSES = ['Delivered', 'Cancelled'];

export function createStatusPoller() {
  throw new Error('not implemented');
}
