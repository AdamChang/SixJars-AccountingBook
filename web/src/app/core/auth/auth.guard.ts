import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { SessionService } from './session.service';

// 401 由 interceptor 整頁導向登入，promise 不會 resolve，所以這裡只需處理成功路徑
export const authGuard: CanActivateFn = async () => {
  await inject(SessionService).load();
  return true;
};
