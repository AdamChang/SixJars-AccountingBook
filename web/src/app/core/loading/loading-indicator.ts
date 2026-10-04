import { HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';

const SHOW_DELAY_MS = 300;

@Injectable({ providedIn: 'root' })
export class LoadingIndicator {
  private readonly _visible = signal(false);
  readonly visible = this._visible.asReadonly();

  private pending = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;

  begin(): void {
    // 只有 0 -> 1 才啟動 timer；快速請求不會閃一下進度條
    if (this.pending++ === 0) {
      this.timer = setTimeout(() => this._visible.set(true), SHOW_DELAY_MS);
    }
  }

  end(): void {
    if (--this.pending === 0) {
      clearTimeout(this.timer);
      this.timer = undefined;
      this._visible.set(false);
    }
  }
}

export const loadingInterceptor: HttpInterceptorFn = (req, next) => {
  const indicator = inject(LoadingIndicator);
  indicator.begin();
  return next(req).pipe(finalize(() => indicator.end()));
};
