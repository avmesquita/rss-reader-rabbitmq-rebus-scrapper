import { Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Article } from './models';

@Component({
  selector: 'app-article-dialog',
  imports: [DatePipe, MatDialogModule],
  templateUrl: './article-dialog.component.html',
  styleUrl: './article-dialog.component.scss'
})
export class ArticleDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<ArticleDialogComponent>);
  readonly article = inject<Article>(MAT_DIALOG_DATA);

  close(): void {
    this.dialogRef.close();
  }

  imageSource(): string | null {
    if (this.article.imageUrl)
      return this.article.imageUrl;
    if (this.article.imageBase64)
      return `data:${this.article.imageMimeType || 'image/jpeg'};base64,${this.article.imageBase64}`;
    return null;
  }
}
