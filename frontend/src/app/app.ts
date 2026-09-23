import { Component, inject, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';

@Component({
  selector: 'app-root',
  imports: [FormsModule, DatePipe],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  private readonly http = inject(HttpClient);
  protected articles: Article[] = [];
  protected feeds: Feed[] = [];
  protected search = '';
  protected feedName = '';
  protected feedUrl = '';
  protected message = '';
  protected category = 'Todas';
  protected feedId = 'Todas';
  protected pageSize = 50;
  protected page = 1;
  protected selectedArticle: Article | null = null;
  protected favoritesOnly = false;
  protected debug: DebugStatus | null = null;

  protected readonly pageSizes = [50, 100, 500];

  protected get pageNumbers(): number[] {
    return Array.from({ length: this.totalPages }, (_, index) => index + 1);
  }

  protected get categories(): string[] {
    return ['Todas', ...new Set(this.articles.map(article => article.category || 'Geral'))];
  }

  protected get visibleArticles(): Article[] {
    const categoryFiltered = this.category === 'Todas'
      ? this.articles
      : this.articles.filter(article => (article.category || 'Geral') === this.category);
    const favoriteFiltered = this.favoritesOnly
      ? categoryFiltered.filter(article => article.isFavorite)
      : categoryFiltered;
    const sourceFiltered = this.feedId === 'Todas'
      ? favoriteFiltered
      : favoriteFiltered.filter(article => article.feedId === this.feedId);
    const start = (this.page - 1) * this.pageSize;
    return this.pageSize === 0 ? sourceFiltered : sourceFiltered.slice(start, start + this.pageSize);
  }

  protected get filteredCount(): number {
    return this.filteredArticles.length;
  }

  protected get totalPages(): number {
    return this.pageSize === 0 ? 1 : Math.max(1, Math.ceil(this.filteredCount / this.pageSize));
  }

  protected get filteredArticles(): Article[] {
    const categoryFiltered = this.category === 'Todas'
      ? this.articles
      : this.articles.filter(article => (article.category || 'Geral') === this.category);
    const favoriteFiltered = this.favoritesOnly
      ? categoryFiltered.filter(article => article.isFavorite)
      : categoryFiltered;
    return this.feedId === 'Todas'
      ? favoriteFiltered
      : favoriteFiltered.filter(article => article.feedId === this.feedId);
  }

  protected changePageSize(): void {
    this.page = 1;
    this.loadArticles();
  }

  protected goToPage(page: number): void {
    this.page = Math.min(Math.max(page, 1), this.totalPages);
  }

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loadArticles();
    this.http.get<Feed[]>('/api/feeds').subscribe(feeds => this.feeds = feeds);
    this.http.get<DebugStatus>('/api/debug').subscribe({ next: debug => this.debug = debug });
  }

  private loadArticles(): void {
    this.http.get<Article[]>('/api/articles', { params: { limit: '0' }}).subscribe({
      next: articles => this.articles = articles,
      error: () => this.message = 'Não foi possível carregar as notícias.'
    });
  }

  protected query(): void {
    const search = this.search.trim();
    this.page = 1;
    this.http.get<Article[]>('/api/articles', { params: search ? { search, limit: '0' } : { limit: '0' } }).subscribe({
      next: articles => this.articles = articles,
      error: () => this.message = 'A busca não pôde ser realizada.'
    });
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

  protected toggleFavorite(article: Article, event: Event): void {
    event.stopPropagation();
    const isFavorite = !article.isFavorite;
    this.http.put<{ id: number; isFavorite: boolean }>(`/api/articles/${article.id}/favorite`, { isFavorite }).subscribe({
      next: result => article.isFavorite = result.isFavorite,
      error: () => this.message = 'Não foi possível atualizar o favorito.'
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

interface Feed { id: string; name: string; url: string; createdAt: string; }
interface Article {
  id: number;
  feedId: string;
  feedName: string;
  title: string;
  url: string;
  excerpt?: string;
  contentText?: string;
  imageUrl?: string;
  imageBase64?: string;
  imageMimeType?: string;
  category?: string;
  isFavorite: boolean;
  collectedAt: string;
}

interface DebugStatus {
  generatedAt: string;
  api: { status: string; recentErrors: ApiError[] };
  rabbit: { available: boolean; error?: string; errorQueues: RabbitQueue[] };
}

interface ApiError { occurredAt: string; method: string; path: string; message: string; }
interface RabbitQueue { name: string; messages: number; messagesReady: number; messagesUnacknowledged: number; consumers: number; }
