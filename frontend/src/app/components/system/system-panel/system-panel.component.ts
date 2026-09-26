import { Component, inject, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { finalize } from 'rxjs';
import { PurgeStats } from '../../../models';
import { SystemService } from '../../../services/system.service';

@Component({
  selector: 'app-system-panel',
  templateUrl: './system-panel.component.html',
  styleUrl: './system-panel.component.scss',
  imports: [
    MatButtonModule
  ]
})
export class SystemPanelComponent implements OnInit {
  private readonly systemService = inject(SystemService);

  refreshing = false;
  message = '';

  purgeStats: PurgeStats = {
    purgedArticles: 0,
    purgedFeeds: 0,
    purgedImages: 0,
    purgedErrors: 0,
    purgedAt: ''
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
}
