import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterOutlet } from '@angular/router';
import { SessionService } from './core/auth/session.service';
import { CurrentBook } from './core/book/current-book';
import { LoadingIndicator } from './core/loading/loading-indicator';

@Component({
  imports: [RouterOutlet, RouterLink, MatButtonModule, MatProgressBarModule, MatToolbarModule],
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly loading = inject(LoadingIndicator);
  protected readonly session = inject(SessionService);
  protected readonly currentBook = inject(CurrentBook);
}
