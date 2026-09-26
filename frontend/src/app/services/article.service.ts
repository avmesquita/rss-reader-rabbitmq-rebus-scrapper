import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { Article, ArticlePage } from '../models';

export interface ArticleQuery {
  page: number;
  pageSize: number;
  favoritesOnly: boolean;
  periodHours: number;
  sort: string;
  search: string;
  category: string;
  feedId: string;
}

@Injectable({ providedIn: 'root' })
export class ArticleService {
  private readonly http = inject(HttpClient);

  getArticles(query: ArticleQuery): Observable<ArticlePage> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize)
      .set('favoritesOnly', query.favoritesOnly)
      .set('sort', query.sort);

    if (query.periodHours > 0) params = params.set('periodHours', query.periodHours);
    if (query.search.trim()) params = params.set('search', query.search.trim());
    if (query.category !== 'Todas') params = params.set('category', query.category);
    if (query.feedId !== 'Todas') params = params.set('feedId', query.feedId);
    return this.http.get<ArticlePage>('/api/articles', { params });
  }

  setFavorite(article: Article, isFavorite: boolean): Observable<{ id: number; isFavorite: boolean }> {
    return this.http.put<{ id: number; isFavorite: boolean }>(
      `/api/articles/${article.id}/favorite`, { isFavorite }
    );
  }

  hideArticle(article: Article): Observable<{ id: number; isHidden: boolean }> {
    return this.http.put<{ id: number; isHidden: boolean }>(
      `/api/articles/${article.id}/hidden`, { isHidden: true }
    );
  }
}
