import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { SessionApi } from '../api/session-api';
import { MeDto } from '../api/dto';
import { BrowserLocation } from '../browser-location';

@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly api = inject(SessionApi);
  private readonly location = inject(BrowserLocation);
  private readonly meState = signal<MeDto | undefined>(undefined);
  private pending: Promise<MeDto> | undefined;

  readonly me = this.meState.asReadonly();

  // 並行呼叫共用同一個 promise；失敗（非 401）時清掉，讓之後可以重試
  load(): Promise<MeDto> {
    this.pending ??= this.fetch().catch((error: unknown) => {
      this.pending = undefined;
      throw error;
    });
    return this.pending;
  }

  async logout(): Promise<void> {
    await firstValueFrom(this.api.logout());
    this.location.assign('/');
  }

  private async fetch(): Promise<MeDto> {
    const me = await firstValueFrom(this.api.me());
    // token 要在登入後取得，之後的非 GET 請求才帶得到 XSRF cookie
    await firstValueFrom(this.api.fetchAntiforgeryToken());
    this.meState.set(me);
    return me;
  }
}
