import config from '../../ngsw-config.json';

describe('ngsw-config', () => {
  it('navigation_excludes_backend_paths', () => {
    const urls = (config as { navigationUrls?: string[] }).navigationUrls ?? [];
    expect(urls).toEqual(expect.arrayContaining([
      '!/auth/**', '!/api/**', '!/health', '/**', '!/**/*.*', '!/**/*__*', '!/**/*__*/**',
    ]));
  });

  it('does_not_cache_api_data', () => {
    const groups = (config as { dataGroups?: unknown[] }).dataGroups ?? [];
    expect(groups).toHaveLength(0);
  });
});
