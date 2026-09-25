import { Component, inject, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatDialog } from '@angular/material/dialog';
import { ArticleDialogComponent } from './article-dialog.component';
import { ArticleListComponent } from './article-list.component';
import { DashboardDialogComponent } from './dashboard-dialog.component';
import { Article, ArticlePage, Feed } from './models';

@Component({
  selector: 'app-root',
  imports: [FormsModule, ArticleListComponent],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly dialog = inject(MatDialog);
  protected articles: Article[] = [];
  protected feeds: Feed[] = [];
  protected search = '';
  protected category = 'Todas';
  protected feedId = 'Todas';
  protected periodHours = 168;
  protected sort = 'published';
  protected pageSize = 10;
  protected page = 1;
  protected totalCount = 0;
  protected totalPageCount = 1;
  protected categories = ['Todas'];
  protected favoritesOnly = false;
  protected secondsUntilRefresh: number | null = null;
  private refreshTimer?: ReturnType<typeof setInterval>;

  protected readonly pageSizes = [10, 25, 50, 100];

  protected get totalPages(): number { return this.totalPageCount; }

  protected get pageOptions(): number[] {
    return Array.from({ length: this.totalPages }, (_, index) => index + 1);
  }

  protected changePageSize(): void {
    this.page = 1;
    this.loadArticles();
  }

  protected goToPage(page: number): void {
    this.page = Math.min(Math.max(Number(page), 1), this.totalPages);
    this.loadArticles();
  }

  ngOnInit(): void {
    this.load();
    this.refreshTimer = setInterval(() => this.updateRefreshCountdown(), 1000);
  }

  ngOnDestroy(): void {
    if (this.refreshTimer)
      clearInterval(this.refreshTimer);
  }

  protected load(): void {
    this.loadArticles();
    this.loadFeeds();
  }

  protected openDashboard(): void {
    const dialogRef = this.dialog.open(DashboardDialogComponent, {
      width: '880px',
      maxWidth: '100vw'
    });
    dialogRef.afterClosed().subscribe(result => {
      if (result?.refresh)
        this.loadFeeds();
    });
  }

  private loadFeeds(): void {
    this.http.get<Feed[]>('/api/feeds').subscribe({
      next: feeds => {
        this.feeds = feeds;
        this.updateRefreshCountdown();
      },
      error: () => this.feeds = []
    });
  }

  protected get refreshLabel(): string {
    if (this.secondsUntilRefresh === null)
      return 'Aguardando o primeiro ciclo';
    if (this.secondsUntilRefresh <= 0)
      return 'Atualização em andamento';
    const hours = Math.floor(this.secondsUntilRefresh / 3600);
    const minutes = Math.floor((this.secondsUntilRefresh % 3600) / 60);
    return hours ? `próxima atualização em ${hours}h ${minutes}min` : `próxima atualização em ${minutes}min`;
  }

  private updateRefreshCountdown(): void {
    const next = this.feeds
      .map(feed => feed.nextScheduledAt ? new Date(feed.nextScheduledAt).getTime() : Number.MAX_SAFE_INTEGER)
      .reduce((earliest, value) => Math.min(earliest, value), Number.MAX_SAFE_INTEGER);
    this.secondsUntilRefresh = next === Number.MAX_SAFE_INTEGER ? null : Math.max(0, Math.floor((next - Date.now()) / 1000));
  }

  private loadArticles(): void {
    this.http.get<ArticlePage>('/api/articles', { params: this.articleParams() }).subscribe({
      next: result => {
        this.articles = result.items;
        this.totalCount = result.totalCount;
        this.totalPageCount = Math.max(1, result.totalPages);
        this.categories = ['Todas', ...result.categories];
        if (this.page !== result.page)
          this.page = result.page;
      },
      error: () => console.error('Não foi possível carregar as notícias.')
    });
  }

  protected query(): void {
    this.page = 1;
    this.loadArticles();
  }

  protected filtersChanged(): void {
    this.page = 1;
    this.loadArticles();
  }

  private articleParams(): Record<string, string> {
    const params: Record<string, string> = {
      page: String(this.page),
      pageSize: String(this.pageSize),
      favoritesOnly: String(this.favoritesOnly)
    };
    if (this.periodHours > 0) params['periodHours'] = String(this.periodHours);
    params['sort'] = this.sort;
    if (this.search.trim()) params['search'] = this.search.trim();
    if (this.category !== 'Todas') params['category'] = this.category;
    if (this.feedId !== 'Todas') params['feedId'] = this.feedId;
    return params;
  }

  protected openArticle(article: Article): void {
    this.dialog.open(ArticleDialogComponent, {
      data: article,
      width: '980px',
      maxWidth: '100vw',
      maxHeight: '100vh'
    });
  }

  protected toggleFavorite(article: Article): void {
    const isFavorite = !article.isFavorite;
    this.http.put<{ id: number; isFavorite: boolean }>(`/api/articles/${article.id}/favorite`, { isFavorite }).subscribe({
      next: result => article.isFavorite = result.isFavorite,
      error: () => console.error('Não foi possível atualizar o favorito.')
    });
  }

  protected hideArticle(article: Article): void {
    this.http.put<{ id: number; isHidden: boolean }>(`/api/articles/${article.id}/hidden`, { isHidden: true }).subscribe({
      next: () => {
        this.loadArticles();
      },
      error: () => console.error('Não foi possível inibir a notícia.')
    });
  }

  
}
