import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { IsActiveMatchOptions, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionService } from './core/auth/session.service';
import { BookCreation } from './core/book/book-creation';
import { CurrentBook } from './core/book/current-book';
import { LoadingIndicator } from './core/loading/loading-indicator';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatButtonModule, MatDividerModule, MatMenuModule, MatProgressBarModule, MatToolbarModule],
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly loading = inject(LoadingIndicator);
  protected readonly session = inject(SessionService);
  protected readonly currentBook = inject(CurrentBook);
  protected readonly bookCreation = inject(BookCreation);

  // 只比對路徑：月份 query params 不同仍算同一頁
  protected readonly activeOptions: IsActiveMatchOptions = {
    paths: 'subset',
    queryParams: 'ignored',
    matrixParams: 'ignored',
    fragment: 'ignored',
  };
}
