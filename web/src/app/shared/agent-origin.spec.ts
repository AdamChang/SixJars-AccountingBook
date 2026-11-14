import { recordedByAgent } from './agent-origin';

describe('recordedByAgent', () => {
  it('mcp: 開頭視為 Agent 記帳', () => {
    expect(recordedByAgent({ createdVia: 'mcp:claude.ai' })).toBe(true);
  });

  it.each([null, undefined, '', 'web'])('%s 不是 Agent 記帳', createdVia => {
    expect(recordedByAgent({ createdVia })).toBe(false);
  });
});
