import { Component, inject, OnDestroy, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-root',
  imports: [FormsModule, DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  protected articles: Article[] = [];
  protected feeds: Feed[] = [];
  protected search = '';
  protected feedName = '';
  protected feedUrl = '';
  protected message = '';
  protected category = 'Todas';
  protected feedId = 'Todas';
  protected periodHours = 168;
  protected sort = 'published';
  protected pageSize = 10;
  protected page = 1;
  protected totalCount = 0;
  protected categories = ['Todas'];
  protected selectedArticle: Article | null = null;
  protected favoritesOnly = false;
  protected debug: DebugStatus | null = null;
  protected dashboard: Dashboard | null = null;
  protected dashboardOpen = false;
  protected secondsUntilRefresh: number | null = null;
  protected refreshingFeedIds = new Set<string>();
  private refreshTimer?: ReturnType<typeof setInterval>;

  protected readonly pageSizes = [10, 25, 50, 100];

  protected get visibleArticles(): Article[] { return this.articles; }

  protected get filteredCount(): number { return this.totalCount; }

  protected get totalPages(): number {
    return Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  protected get pageOptions(): number[] {
    return Array.from({ length: this.totalPages }, (_, index) => index + 1);
  }

  protected changePageSize(): void {
    this.page = 1;
    this.loadArticles();
  }

  protected goToPage(page: number): void {
    this.page = Math.min(Math.max(page, 1), this.totalPages);
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
    this.loadDashboard();
    this.http.get<DebugStatus>('/api/debug').subscribe({ next: debug => this.debug = debug });
  }

  protected openDashboard(): void {
    this.dashboardOpen = true;
    this.loadDashboard();
    this.loadDebug();
  }

  protected closeDashboard(): void {
    this.dashboardOpen = false;
  }

  private loadDashboard(): void {
    this.http.get<Dashboard>('/api/dashboard').subscribe({
      next: dashboard => {
        this.dashboard = dashboard;
        this.feeds = dashboard.feeds;
        this.updateRefreshCountdown();
      },
      error: () => this.http.get<Feed[]>('/api/feeds').subscribe(feeds => this.feeds = feeds)
    });
  }

  private loadDebug(): void {
    this.http.get<DebugStatus>('/api/debug').subscribe({ next: debug => this.debug = debug });
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
        this.categories = ['Todas', ...result.categories];
        if (this.page !== result.page)
          this.page = result.page;
      },
      error: () => this.message = 'Não foi possível carregar as notícias.'
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

  protected imageSource(article: Article): string | null {
    if (article.imageUrl)
      return article.imageUrl;
    if (article.imageBase64)
      return `data:${article.imageMimeType || 'image/jpeg'};base64,${article.imageBase64}`;
    return null;
  }

  protected addFeed(): void {
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

  protected refreshFeed(feed: Feed, event: Event): void {
    event.stopPropagation();
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

  protected isRefreshing(feed: Feed): boolean {
    return this.refreshingFeedIds.has(feed.id);
  }

  protected toggleFavorite(article: Article, event: Event): void {
    event.stopPropagation();
    const isFavorite = !article.isFavorite;
    this.http.put<{ id: number; isFavorite: boolean }>(`/api/articles/${article.id}/favorite`, { isFavorite }).subscribe({
      next: result => article.isFavorite = result.isFavorite,
      error: () => this.message = 'Não foi possível atualizar o favorito.'
    });
  }

  protected hideArticle(article: Article, event: Event): void {
    event.stopPropagation();
    this.http.put<{ id: number; isHidden: boolean }>(`/api/articles/${article.id}/hidden`, { isHidden: true }).subscribe({
      next: () => {
        this.loadArticles();
        this.message = 'Notícia inibida.';
      },
      error: () => this.message = 'Não foi possível inibir a notícia.'
    });
  }

  protected deleteFeed(feed: Feed, event: Event): void {
    event.stopPropagation();
    if (!window.confirm(`Excluir a fonte "${feed.name}" e os artigos coletados dela?`))
      return;

    this.http.delete(`/api/feeds/${feed.id}`).subscribe({
      next: () => {
        this.feeds = this.feeds.filter(item => item.id !== feed.id);
        this.articles = this.articles.filter(article => article.feedId !== feed.id);
        if (this.feedId === feed.id)
          this.feedId = 'Todas';
        this.message = `Fonte "${feed.name}" excluída.`;
      },
      error: () => this.message = 'Não foi possível excluir a fonte.'
    });
  }
}

interface Feed { id: string; name: string; url: string; createdAt: string; lastCheckedAt?: string; nextScheduledAt?: string; lastError?: string; lastCollectedCount: number; }
interface Article {
  id: number;
  feedId: string;
  feedName: string;
  title: string;
  url: string;
  author?: string;
  excerpt?: string;
  contentHtml?: string;
  contentText?: string;
  imageUrl?: string;
  imageBase64?: string;
  imageMimeType?: string;
  category?: string;
  publishedAt?: string;
  isFavorite: boolean;
  isHidden: boolean;
  collectedAt: string;
}

interface Dashboard { stats: { totalArticles: number; visibleArticles: number; hiddenArticles: number; favorites: number }; feeds: Feed[]; }
interface ArticlePage { items: Article[]; totalCount: number; page: number; pageSize: number; totalPages: number; categories: string[]; }

interface DebugStatus {
  generatedAt: string;
  api: { status: string; recentErrors: ApiError[] };
  rabbit: { available: boolean; error?: string; errorQueues: RabbitQueue[] };
}

interface ApiError { occurredAt: string; method: string; path: string; message: string; }
interface RabbitQueue { name: string; messages: number; messagesReady: number; messagesUnacknowledged: number; consumers: number; }
