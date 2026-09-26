import { Component, inject, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Feed } from '../../../models';
import { FeedService } from '../../../services/feed.service';

@Component({
  selector: 'app-feed-management',
  imports: [DatePipe, FormsModule],
  templateUrl: './feed-management.component.html',
  styleUrl: './feed-management.component.scss'
})
export class FeedManagementComponent implements OnInit {
  private readonly feedService = inject(FeedService);

  feeds: Feed[] = [];
  feedName = '';
  feedUrl = '';
  message = '';
  refreshingFeedIds = new Set<string>();

  ngOnInit(): void {
    this.loadFeeds();
  }

  loadFeeds(): void {
    this.feedService.getFeeds().subscribe({
      next: feeds => this.feeds = feeds,
      error: () => this.feeds = []
    });
  }

  addFeed(): void {
    this.feedService.createFeed(this.feedName, this.feedUrl).subscribe({
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
    this.feedService.refreshFeed(feed.id).subscribe({
      next: () => {
        this.message = `Atualização de "${feed.name}" enviada.`;
        this.loadFeeds();
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

    this.feedService.deleteFeed(feed.id).subscribe({
      next: () => {
        this.feeds = this.feeds.filter(item => item.id !== feed.id);
        this.message = `Fonte "${feed.name}" excluída.`;
      },
      error: () => this.message = 'Não foi possível excluir a fonte.'
    });
  }
}
