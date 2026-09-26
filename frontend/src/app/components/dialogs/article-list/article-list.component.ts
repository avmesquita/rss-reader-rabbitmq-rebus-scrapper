import { Component, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Article } from '../../../models';
import { articleImageSource } from '../../../utils/article-image.util';

@Component({
  selector: 'app-article-list',
  imports: [DatePipe, FormsModule, MatButtonModule ],
  templateUrl: './article-list.component.html',
  styleUrl: './article-list.component.scss'
})
export class ArticleListComponent {
  readonly articles = input.required<Article[]>();
  readonly page = input.required<number>();
  readonly totalPages = input.required<number>();
  readonly pageOptions = input.required<number[]>();

  readonly selected = output<Article>();
  readonly favoriteToggled = output<Article>();
  readonly hidden = output<Article>();
  readonly pageChanged = output<number>();

  imageSource(article: Article): string | null {
    return articleImageSource(article);
  }

  selectArticle(article: Article): void {
    this.selected.emit(article);
  }

  toggleFavorite(article: Article, event: Event): void {
    event.stopPropagation();
    this.favoriteToggled.emit(article);
  }

  hideArticle(article: Article, event: Event): void {
    event.stopPropagation();
    this.hidden.emit(article);
  }

  changePage(page: number): void {
    this.pageChanged.emit(Number(page));
  }
}
