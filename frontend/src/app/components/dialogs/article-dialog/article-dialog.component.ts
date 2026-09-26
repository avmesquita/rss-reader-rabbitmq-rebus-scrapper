import { Component, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Article } from '../../../models';
import { articleImageSource } from '../../../utils/article-image.util';

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
    return articleImageSource(this.article);
  }
}
