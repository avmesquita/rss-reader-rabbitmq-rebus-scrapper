import { Component, inject, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Dashboard, DashboardTab, DebugStatus, Feed } from './models';

@Component({
  selector: 'app-dashboard-dialog',
  imports: [DatePipe, FormsModule, MatDialogModule],
  templateUrl: './dashboard-dialog.component.html',
  styleUrl: './dashboard-dialog.component.scss'
})
export class DashboardDialogComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly dialogRef = inject(MatDialogRef<DashboardDialogComponent>);

  dashboard: Dashboard | null = null;
  feeds: Feed[] = [];
  debug: DebugStatus | null = null;
  tab: DashboardTab = 'stats';
  feedName = '';
  feedUrl = '';
  message = '';
  telemetryRefreshing = false;
  refreshingFeedIds = new Set<string>();

  ngOnInit(): void {
    this.loadDashboard();
    this.loadDebug();
  }

  close(): void {
    this.dialogRef.close({ refresh: true });
  }

  loadDashboard(): void {
    this.http.get<Dashboard>('/api/dashboard').subscribe({
      next: dashboard => {
        this.dashboard = dashboard;
        this.feeds = dashboard.feeds;
      },
      error: () => this.http.get<Feed[]>('/api/feeds').subscribe(feeds => this.feeds = feeds)
    });
  }

  loadDebug(): void {
    this.http.get<DebugStatus>('/api/debug').subscribe({ next: debug => this.debug = debug });
  }

  refreshTelemetry(): void {
    this.telemetryRefreshing = true;
    this.http.get<DebugStatus>('/api/debug').subscribe({
      next: debug => this.debug = debug,
      error: () => this.message = 'Não foi possível atualizar a telemetria.',
      complete: () => this.telemetryRefreshing = false
    });
  }

  addFeed(): void {
    this.http.post<Feed>('/api/feeds', { name: this.feedName, url: this.feedUrl }).subscribe({
      next: feed => {
        this.feeds = [feed, ...this.feeds];
        this.feedName = '';
        this.feedUrl = '';
        this.message = 'Concentrador incluído e enviado para processamento.';
      },
      error: () => this.message = 'Não foi possível incluir o concentrador.'
    });
  }

  refreshFeed(feed: Feed): void {
    this.refreshingFeedIds.add(feed.id);
    this.http.post(`/api/feeds/${feed.id}/refresh`, {}).subscribe({
      next: () => {
        this.message = `Atualização de "${feed.name}" enviada.`;
        this.loadDashboard();
      },
      error: error => {
        this.message = error.error?.detail || `Não foi possível atualizar "${feed.name}".`;
        this.refreshingFeedIds.delete(feed.id);
      },
      complete: () => this.refreshingFeedIds.delete(feed.id)
    });
  }

  isRefreshing(feed: Feed): boolean {
    return this.refreshingFeedIds.has(feed.id);
  }

  deleteFeed(feed: Feed): void {
    if (!window.confirm(`Excluir a fonte "${feed.name}" e os artigos coletados dela?`))
      return;

    this.http.delete(`/api/feeds/${feed.id}`).subscribe({
      next: () => {
        this.feeds = this.feeds.filter(item => item.id !== feed.id);
        this.message = `Fonte "${feed.name}" excluída.`;
      },
      error: () => this.message = 'Não foi possível excluir a fonte.'
    });
  }
}
