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
  feedDescription = '';
  feedPollIntervalMinutes = 0;
  editingFeedId: string | null = null;
  editName = '';
  editDescription = '';
  editPollIntervalMinutes = 0;
  message = '';
  editMessage = '';
  savingFeedId: string | null = null;
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
    this.feedService.createFeed(this.feedName, this.feedUrl, this.feedDescription, this.feedPollIntervalMinutes).subscribe({
      next: feed => {
        this.feeds = [feed, ...this.feeds];
        this.feedName = '';
        this.feedUrl = '';
        this.feedDescription = '';
        this.feedPollIntervalMinutes = 0;
        this.message = 'Concentrador incluído e enviado para processamento.';
      },
      error: () => this.message = 'Não foi possível incluir o concentrador.'
    });
  }

  startEdit(feed: Feed): void {
    this.editingFeedId = feed.id;
    this.editName = feed.name;
    this.editDescription = feed.description ?? '';
    this.editPollIntervalMinutes = feed.pollIntervalMinutes;
    this.editMessage = '';
  }

  cancelEdit(): void {
    this.editingFeedId = null;
  }

  saveFeed(feed: Feed): void {
    this.savingFeedId = feed.id;
    this.editMessage = '';
    this.feedService.updateFeed(feed, this.editName, this.editDescription, this.editPollIntervalMinutes).subscribe({
      next: updated => {
        this.feeds = this.feeds.map(item => item.id === updated.id ? updated : item);
        this.editingFeedId = null;
        this.message = `Configurações de "${updated.name}" salvas.`;
      },
      error: error => {
        this.editMessage = error.error?.detail || error.error?.error || 'Não foi possível salvar as configurações da fonte.';
        this.savingFeedId = null;
      },
      complete: () => this.savingFeedId = null
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
