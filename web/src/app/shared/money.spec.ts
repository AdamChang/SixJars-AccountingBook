import { formatAmount } from './money';

describe('formatAmount', () => {
  it.each([
    [-1234.5, '-1,234.5'],
    [1000, '1,000'],
    [0.125, '0.13'],
  ])('formats %s as %s', (amount, expected) => {
    expect(formatAmount(amount)).toBe(expected);
  });
});
