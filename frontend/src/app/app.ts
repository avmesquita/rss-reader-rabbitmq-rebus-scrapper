import { Component, HostListener, inject, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatDialog } from '@angular/material/dialog';
import { ArticleDialogComponent } from './components/dialogs/article-dialog/article-dialog.component';
import { ArticleListComponent } from './components/dialogs/article-list/article-list.component';
import { DashboardDialogComponent } from './components/dialogs/dashboard-dialog/dashboard-dialog.component';
import { Article, Feed } from './models';
import { ArticleQuery, ArticleService } from './services/article.service';
import { FeedService } from './services/feed.service';
import { MatButtonModule } from '@angular/material/button';

interface InstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
}

interface SavedViewState {
  search: string;
  category: string;
  feedId: string;
  periodHours: number;
  sort: string;
  pageSize: number;
  page: number;
  favoritesOnly: boolean;
  articleLayout: 'list' | 'grid';
}

@Component({
  selector: 'app-root',
  imports: [FormsModule, ArticleListComponent, MatButtonModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit, OnDestroy {
  private readonly viewStateKey = 'rss-reader.view-state.v1';
  private readonly articleService = inject(ArticleService);
  private readonly feedService = inject(FeedService);
  private readonly dialog = inject(MatDialog);
  protected articles: Article[] = [];
  protected feeds: Feed[] = [];
  protected search = '';
  protected category = 'Todas';
  protected feedId = 'Todas';
  protected periodHours = 168;
  protected sort = 'published';
  protected pageSize = 10;
  protected articleLayout: 'list' | 'grid' = 'list';
  protected page = 1;
  protected totalCount = 0;
  protected totalPageCount = 1;
  protected categories = ['Todas'];
  protected favoritesOnly = false;
  protected secondsUntilRefresh: number | null = null;
  protected installPrompt: InstallPromptEvent | null = null;
  private refreshTimer?: ReturnType<typeof setInterval>;

  protected readonly pageSizes = [10, 25, 50, 100];

  @HostListener('window:beforeinstallprompt', ['$event'])
  captureInstallPrompt(event: Event): void {
    event.preventDefault();
    this.installPrompt = event as InstallPromptEvent;
  }

  @HostListener('window:appinstalled')
  appInstalled(): void {
    this.installPrompt = null;
  }

  protected async installApp(): Promise<void> {
    if (!this.installPrompt)
      return;
    const prompt = this.installPrompt;
    this.installPrompt = null;
    await prompt.prompt();
    await prompt.userChoice;
  }

  protected get totalPages(): number { return this.totalPageCount; }

  protected get pageOptions(): number[] {
    return Array.from({ length: this.totalPages }, (_, index) => index + 1);
  }

  protected changePageSize(): void {
    this.page = 1;
    this.saveViewState();
    this.loadArticles();
  }

  protected goToPage(page: number): void {
    this.page = Math.min(Math.max(Number(page), 1), this.totalPages);
    this.saveViewState();
    this.loadArticles();
  }

  ngOnInit(): void {
    this.restoreViewState();
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
      maxWidth: '100vw',
      minHeight: '90vh'
    });
    dialogRef.afterClosed().subscribe(result => {
      if (result?.refresh)
        this.loadFeeds();
    });
  }

  private loadFeeds(): void {
    this.feedService.getFeeds().subscribe({
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
    this.articleService.getArticles(this.articleQuery()).subscribe({
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
    this.saveViewState();
    this.loadArticles();
  }

  protected filtersChanged(): void {
    this.page = 1;
    this.saveViewState();
    this.loadArticles();
  }

  protected toggleFavorites(): void {
    this.favoritesOnly = !this.favoritesOnly;
    this.filtersChanged();
  }

  protected setArticleLayout(layout: 'list' | 'grid'): void {
    this.articleLayout = layout;
    this.saveViewState();
  }

  private restoreViewState(): void {
    try {
      const raw = localStorage.getItem(this.viewStateKey);
      if (!raw)
        return;
      const saved = JSON.parse(raw) as Partial<SavedViewState>;
      if (typeof saved.search === 'string') this.search = saved.search;
      if (typeof saved.category === 'string') this.category = saved.category;
      if (typeof saved.feedId === 'string') this.feedId = saved.feedId;
      if ([0, 24, 168, 720].includes(Number(saved.periodHours))) this.periodHours = Number(saved.periodHours);
      if (saved.sort === 'published' || saved.sort === 'collected') this.sort = saved.sort;
      if ([0, ...this.pageSizes].includes(Number(saved.pageSize))) this.pageSize = Number(saved.pageSize);
      if (Number.isInteger(saved.page) && Number(saved.page) > 0) this.page = Number(saved.page);
      if (typeof saved.favoritesOnly === 'boolean') this.favoritesOnly = saved.favoritesOnly;
      if (saved.articleLayout === 'list' || saved.articleLayout === 'grid') this.articleLayout = saved.articleLayout;
    } catch {
      // Storage may be unavailable or contain invalid data; use the default view.
    }
  }

  private saveViewState(): void {
    const state: SavedViewState = {
      search: this.search,
      category: this.category,
      feedId: this.feedId,
      periodHours: this.periodHours,
      sort: this.sort,
      pageSize: this.pageSize,
      page: this.page,
      favoritesOnly: this.favoritesOnly,
      articleLayout: this.articleLayout
    };
    try {
      localStorage.setItem(this.viewStateKey, JSON.stringify(state));
    } catch {
      // The app remains usable when browser storage is disabled or full.
    }
  }

  private articleQuery(): ArticleQuery {
    return {
      page: this.page,
      pageSize: this.pageSize,
      favoritesOnly: this.favoritesOnly,
      periodHours: this.periodHours,
      sort: this.sort,
      search: this.search,
      category: this.category,
      feedId: this.feedId
    };
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
    this.articleService.setFavorite(article, isFavorite).subscribe({
      next: result => article.isFavorite = result.isFavorite,
      error: () => console.error('Não foi possível atualizar o favorito.')
    });
  }

  protected hideArticle(article: Article): void {
    this.articleService.hideArticle(article).subscribe({
      next: () => {
        this.loadArticles();
      },
      error: () => console.error('Não foi possível inibir a notícia.')
    });
  }

  
}
