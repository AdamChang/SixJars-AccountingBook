import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { TransactionDto, TransactionInput } from './dto';

// URL 必須是 '/' 開頭的相對路徑，Angular 的 XSRF interceptor 只對相對 URL 加 header
// 篩選只套用在當月（tags frontend spec F8、transaction filters Q5）；沒有值的條件不帶參數，順序固定（F1）
export interface TransactionFilter {
  tagId?: string | null;
  categoryId?: string | null;
  accountId?: string | null;
}

@Injectable({ providedIn: 'root' })
export class TransactionApi {
  private readonly http = inject(HttpClient);

  list(bookId: string, budgetMonth: number, filter: TransactionFilter = {}): Observable<TransactionDto[]> {
    let params = new HttpParams().set('budgetMonth', budgetMonth);
    if (filter.tagId) {
      params = params.set('tagId', filter.tagId);
    }
    if (filter.categoryId) {
      params = params.set('categoryId', filter.categoryId);
    }
    if (filter.accountId) {
      params = params.set('accountId', filter.accountId);
    }
    return this.http.get<TransactionDto[]>(`/api/books/${bookId}/transactions`, { params });
  }

  create(bookId: string, input: TransactionInput): Observable<TransactionDto> {
    return this.http.post<TransactionDto>(`/api/books/${bookId}/transactions`, input);
  }

  update(bookId: string, id: string, version: number, input: TransactionInput): Observable<TransactionDto> {
    return this.http.put<TransactionDto>(`/api/books/${bookId}/transactions/${id}`, { version, input });
  }

  delete(bookId: string, id: string, version: number): Observable<void> {
    const params = new HttpParams().set('version', version);
    return this.http.delete<void>(`/api/books/${bookId}/transactions/${id}`, { params });
  }
}
