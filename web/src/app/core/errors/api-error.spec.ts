import { HttpErrorResponse } from '@angular/common/http';
import { ApiError, classifyError } from './api-error';
import { DOMAIN_FALLBACK_MESSAGE } from './messages';

const res = (status: number, error: unknown = null) =>
  new HttpErrorResponse({ status, error });

describe('classifyError', () => {
  const cases: [string, HttpErrorResponse, ApiError][] = [
    [
      '400 with errors -> validation',
      res(400, { errors: { counterAccountId: ['必填'] } }),
      { kind: 'validation', fieldErrors: { counterAccountId: ['必填'] } },
    ],
    ['400 antiforgery title', res(400, { title: '缺少或無效的 XSRF token' }), { kind: 'antiforgery' }],
    ['401', res(401), { kind: 'unauthorized' }],
    ['404', res(404), { kind: 'notFound' }],
    ['409', res(409), { kind: 'conflict' }],
    [
      '422 with code and detail',
      res(422, { code: 'locked', detail: '已鎖帳' }),
      { kind: 'domain', code: 'locked', message: '已鎖帳' },
    ],
    ['422 without code -> rule', res(422, {}), { kind: 'domain', code: 'rule', message: DOMAIN_FALLBACK_MESSAGE }],
    ['422 with blank detail -> fallback', res(422, { code: 'x', detail: '  ' }), { kind: 'domain', code: 'x', message: DOMAIN_FALLBACK_MESSAGE }],
    ['500', res(500), { kind: 'unavailable' }],
    ['status 0', res(0), { kind: 'unavailable' }],
    ['other 400 (malformed json)', res(400, { title: '請求格式錯誤' }), { kind: 'unavailable' }],
    ['400 with null body', res(400), { kind: 'unavailable' }],
  ];

  it.each(cases)('%s', (_name, response, expected) => {
    expect(classifyError(response)).toEqual(expected);
  });
});
