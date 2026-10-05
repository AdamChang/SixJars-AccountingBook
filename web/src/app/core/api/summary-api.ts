import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { LedgerSummaryDto } from './dto';

@Injectable({ providedIn: 'root' })
export class SummaryApi {
  private readonly http = inject(HttpClient);

  // 不帶 asOf，由後端依 budgetMonth 決定截止日
  get(bookId: string, budgetMonth: number): Observable<LedgerSummaryDto> {
    const params = new HttpParams().set('budgetMonth', budgetMonth);
    return this.http.get<LedgerSummaryDto>(`/api/books/${bookId}/summary`, { params });
  }
}
