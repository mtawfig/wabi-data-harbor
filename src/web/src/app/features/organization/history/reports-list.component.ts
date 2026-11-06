import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { HistoryService } from '../../../core/history/history.service';
import { ExportFormat, ReportFile } from '../../../core/history/history.models';
import { problemMessage } from '../../../core/problem-message';

/** My Reports (specification §20): every study PDF and export the signed-in user generated, with download links back to the analysis. */
@Component({
  selector: 'app-reports-list',
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule, MatSelectModule, MatTableModule, TranslocoModule],
  templateUrl: './reports-list.component.html',
  styleUrl: './reports-list.component.scss',
})
export class ReportsListComponent {
  readonly history = inject(HistoryService);
  private readonly transloco = inject(TranslocoService);

  readonly items = signal<ReportFile[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(25);
  readonly search = signal('');
  readonly format = signal<ExportFormat | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly columns = ['file', 'analysis', 'format', 'size', 'created', 'download'];
  readonly formats: ExportFormat[] = ['Pdf', 'Docx', 'Xlsx'];

  constructor() {
    queueMicrotask(() => this.load());
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.history.reports(this.page(), this.pageSize(), this.search().trim() || undefined, this.format()).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.total.set(result.totalCount);
        this.loading.set(false);
      },
      error: (e: HttpErrorResponse) => {
        this.loading.set(false);
        this.error.set(problemMessage(e, this.transloco));
      },
    });
  }

  applyFilters(): void {
    this.page.set(1);
    this.load();
  }

  onPage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  icon(format: ExportFormat): string {
    return format === 'Pdf' ? 'picture_as_pdf' : format === 'Docx' ? 'description' : 'table_view';
  }
}
