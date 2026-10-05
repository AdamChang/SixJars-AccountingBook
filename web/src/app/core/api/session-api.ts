import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { MeDto } from './dto';

@Injectable({ providedIn: 'root' })
export class SessionApi {
  private readonly http = inject(HttpClient);

  me(): Observable<MeDto> {
    return this.http.get<MeDto>('/api/me');
  }

  fetchAntiforgeryToken(): Observable<void> {
    return this.http.get<void>('/api/antiforgery/token');
  }

  logout(): Observable<void> {
    return this.http.post<void>('/auth/logout', null);
  }
}
