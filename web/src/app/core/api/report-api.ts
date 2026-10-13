import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { MonthlyReportDto, YearlyReportDto } from './dto';

// URL 必須是 '/' 開頭的相對路徑，Angular 的 XSRF interceptor 只對相對 URL 加 header
@Injectable({ providedIn: 'root' })
export class ReportApi {
  private readonly http = inject(HttpClient);

  monthly(bookId: string, budgetMonth: number): Observable<MonthlyReportDto> {
    return this.http.get<MonthlyReportDto>(`/api/books/${bookId}/reports/monthly`, {
      params: new HttpParams().set('budgetMonth', budgetMonth),
    });
  }

  yearly(bookId: string, year: number): Observable<YearlyReportDto> {
    return this.http.get<YearlyReportDto>(`/api/books/${bookId}/reports/yearly`, { params: new HttpParams().set('year', year) });
  }
}
