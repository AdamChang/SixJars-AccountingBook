import { FormControl } from '@angular/forms';
import { BudgetRowDto, CategoryBudgetDto } from '../../core/api/dto';
import { applyBudget, budgetAmountValidator, isOverBudget, parseBudgetAmount, sourceLabel, usagePercent } from './budget-rules';

const ROW: BudgetRowDto = { categoryId: 'cat-food', defaultAmount: null, budget: null, source: null, actual: 1000, remaining: null };

describe('budget-rules', () => {
  it('parse_accepts_integers_decimals_and_thousands_separators', () => {
    expect(parseBudgetAmount('5000')).toBe(5000);
    expect(parseBudgetAmount(' 1,200.5 ')).toBe(1200.5);
    expect(parseBudgetAmount('0')).toBe(0);
  });

  it('parse_rejects_empty_negative_and_garbage', () => {
    for (const text of ['', '   ', '-5', '12a', '1.234', '.5']) {
      expect(parseBudgetAmount(text)).toBeNull();
    }
  });

  it('validator_flags_invalid_text', () => {
    expect(budgetAmountValidator(new FormControl('abc'))).toEqual({ budgetAmount: true });
    expect(budgetAmountValidator(new FormControl(''))).toEqual({ budgetAmount: true });
    expect(budgetAmountValidator(new FormControl('300'))).toBeNull();
  });

  it('source_label', () => {
    expect(sourceLabel('Override')).toBe('本月');
    expect(sourceLabel('Default')).toBe('預設');
    expect(sourceLabel(null)).toBe('未設');
  });

  it('usage_percent_handles_unset_zero_budget_and_overspending', () => {
    expect(usagePercent({ budget: null, actual: 100 })).toBeNull();
    expect(usagePercent({ budget: 0, actual: 0 })).toBe(0);
    expect(usagePercent({ budget: 0, actual: 1 })).toBe(100);
    expect(usagePercent({ budget: 4000, actual: 1000 })).toBe(25);
    expect(usagePercent({ budget: 1000, actual: 1250 })).toBe(125);
  });

  it('is_over_budget_only_when_actual_exceeds_budget', () => {
    expect(isOverBudget({ budget: 1000, actual: 1000 })).toBe(false);
    expect(isOverBudget({ budget: 1000, actual: 1000.5 })).toBe(true);
    expect(isOverBudget({ budget: null, actual: 99999 })).toBe(false);
  });

  it('apply_budget_prefers_override_even_when_zero', () => {
    const budget: CategoryBudgetDto = { categoryId: 'cat-food', defaultAmount: 5000, overrides: [{ budgetMonth: 202602, amount: 0 }] };
    expect(applyBudget(ROW, budget, 202602)).toEqual({ ...ROW, defaultAmount: 5000, budget: 0, source: 'Override', remaining: -1000 });
  });

  it('apply_budget_falls_back_to_default_then_unset', () => {
    const withDefault: CategoryBudgetDto = { categoryId: 'cat-food', defaultAmount: 3000, overrides: [{ budgetMonth: 202601, amount: 1 }] };
    expect(applyBudget(ROW, withDefault, 202602)).toEqual({ ...ROW, defaultAmount: 3000, budget: 3000, source: 'Default', remaining: 2000 });
    const overrideElsewhere: CategoryBudgetDto = { categoryId: 'cat-food', defaultAmount: null, overrides: [{ budgetMonth: 202601, amount: 1 }] };
    expect(applyBudget(ROW, overrideElsewhere, 202602)).toEqual(ROW);
  });
});
