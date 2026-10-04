import { addMonths, budgetMonthOf, formatBudgetMonth, parseBudgetMonth, parseDateString, toDateString } from './dates';

describe('dates', () => {
  it.each([
    [new Date(2026, 0, 1, 0, 30), '2026-01-01'],
    [new Date(2026, 11, 31, 23, 59), '2026-12-31'],
    [new Date(2026, 1, 5), '2026-02-05'],
  ])('toDateString uses local year/month/day (%s)', (date, expected) => {
    expect(toDateString(date)).toBe(expected);
  });

  it('parseDateString returns local midnight', () => {
    const date = parseDateString('2026-03-01');
    expect(date.getDate()).toBe(1);
    expect(date.getHours()).toBe(0);
    expect(date.getMonth()).toBe(2);
  });

  it.each([
    ['2026-01-31', 202601],
    [new Date(2026, 0, 1, 0, 30), 202601],
  ])('budgetMonthOf(%s)', (input, expected) => {
    expect(budgetMonthOf(input)).toBe(expected);
  });

  it.each([
    [202601, -1, 202512],
    [202612, 1, 202701],
    [202601, 13, 202702],
  ])('addMonths(%i, %i)', (month, delta, expected) => {
    expect(addMonths(month, delta)).toBe(expected);
  });

  it.each([
    ['202601', 202601],
    ['202613', null],
    ['202600', null],
    ['abc', null],
    ['20261', null],
    ['', null],
    [null, null],
  ])('parseBudgetMonth(%s)', (value, expected) => {
    expect(parseBudgetMonth(value)).toBe(expected);
  });

  it('formatBudgetMonth', () => {
    expect(formatBudgetMonth(202601)).toBe('2026/01');
  });
});
