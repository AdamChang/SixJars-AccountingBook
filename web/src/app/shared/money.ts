const formatter = new Intl.NumberFormat('zh-TW', { maximumFractionDigits: 2 });

export function formatAmount(amount: number): string {
  return formatter.format(amount);
}
