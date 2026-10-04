describe('test environment', () => {
  it('runs_in_asia_taipei', () => {
    expect(new Date(2026, 0, 1, 0, 30).getTimezoneOffset()).toBe(-480);
  });
});
