import { HttpErrorResponse } from '@angular/common/http';
import { DOMAIN_FALLBACK_MESSAGE } from './messages';

export type ApiError =
  | { kind: 'validation'; fieldErrors: Record<string, string[]> }
  | { kind: 'domain'; code: string; message: string }
  | { kind: 'conflict' }
  | { kind: 'notFound' }
  | { kind: 'antiforgery' }
  | { kind: 'unauthorized' }
  | { kind: 'unavailable' };

// 對應後端 AntiforgeryFilter.InvalidTokenTitle
const ANTIFORGERY_TITLE = '缺少或無效的 XSRF token';

export function classifyError(error: HttpErrorResponse): ApiError {
  const body = (error.error ?? null) as {
    errors?: Record<string, string[]>;
    title?: string;
    code?: string;
    detail?: string;
  } | null;

  switch (error.status) {
    case 400:
      if (body?.errors && typeof body.errors === 'object') {
        return { kind: 'validation', fieldErrors: body.errors };
      }
      if (body?.title === ANTIFORGERY_TITLE) {
        return { kind: 'antiforgery' };
      }
      return { kind: 'unavailable' };
    case 401:
      return { kind: 'unauthorized' };
    case 404:
      return { kind: 'notFound' };
    case 409:
      return { kind: 'conflict' };
    case 422:
      return { kind: 'domain', code: body?.code ?? 'rule', message: body?.detail?.trim() ? body.detail : DOMAIN_FALLBACK_MESSAGE };
    default:
      return { kind: 'unavailable' };
  }
}
