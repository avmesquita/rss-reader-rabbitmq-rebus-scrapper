import { Component, inject, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { finalize } from 'rxjs';
import { PurgeStats, ReadDeletionStats } from '../../../models';
import { SystemService } from '../../../services/system.service';

@Component({
  selector: 'app-system-panel',
  templateUrl: './system-panel.component.html',
  styleUrl: './system-panel.component.scss',
  imports: [
    MatButtonModule,
    DatePipe
  ]
})
export class SystemPanelComponent implements OnInit {
  private readonly systemService = inject(SystemService);

  refreshing = false;
  message = '';
  readDeletionStats: ReadDeletionStats | null = null;

  purgeStats: PurgeStats = {
    purgedArticles: 0,
    purgedFeeds: 0,
    purgedImages: 0,
    purgedErrors: 0,
    purgedAt: '',
    before: { all: 0, read: 0, hidden: 0, favorites: 0 },
    after: { all: 0, read: 0, hidden: 0, favorites: 0 }
  };

  ngOnInit(): void {
    
  }

  refresh(): void {
    this.refreshing = true;
    this.message = '';
    //this.loadTelemetry();
  }

  public purgeData(): void {
    this.refreshing = true;
    this.message = '';
    this.systemService.purgeData().pipe(
      finalize(() => this.refreshing = false)
    ).subscribe({
      next: stats => {
        this.purgeStats = stats;
        this.message = 'Limpeza concluída.';
      },
      error: () => this.message = 'Não foi possível limpar os dados.'
    });
  }

  public deleteReadArticles(): void {
    if (!window.confirm('Excluir todas as notícias lidas que não são favoritas? Elas não serão coletadas novamente.'))
      return;

    this.refreshing = true;
    this.message = '';
    this.systemService.deleteReadArticles().pipe(
      finalize(() => this.refreshing = false)
    ).subscribe({
      next: stats => {
        this.readDeletionStats = stats;
        this.message = 'Exclusão de notícias lidas concluída.';
      },
      error: () => this.message = 'Não foi possível excluir as notícias lidas.'
    });
  }
}
