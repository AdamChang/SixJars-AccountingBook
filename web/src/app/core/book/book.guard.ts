import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { ApiError } from '../errors/api-error';
import { CurrentBook } from './current-book';

export const bookGuard: CanActivateFn = async (route) => {
  const bookId = route.paramMap.get('bookId');
  const router = inject(Router);
  if (!bookId) {
    return router.parseUrl('/');
  }
  try {
    await inject(CurrentBook).load(bookId);
    return true;
  } catch (error) {
    // 只有帳本不存在才導回首頁；其他錯誤交給全域處理
    if ((error as Partial<ApiError> | null)?.kind === 'notFound') {
      return router.parseUrl('/');
    }
    throw error;
  }
};
