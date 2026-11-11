import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { LedgerSummaryDto } from './dto';

@Injectable({ providedIn: 'root' })
export class SummaryApi {
  private readonly http = inject(HttpClient);

  // asOf 省略時後端以台北時間的今天截止
  get(bookId: string, budgetMonth: number, asOf?: string): Observable<LedgerSummaryDto> {
    let params = new HttpParams().set('budgetMonth', budgetMonth);
    if (asOf) {
      params = params.set('asOf', asOf);
    }
    return this.http.get<LedgerSummaryDto>(`/api/books/${bookId}/summary`, { params });
  }
}
