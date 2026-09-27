import { Component, inject, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { finalize } from 'rxjs';
import { Dashboard } from '../../../models';
import { DashboardService } from '../../../services/dashboard.service';

@Component({
  selector: 'app-statistics-panel',
  imports: [MatButtonModule],
  templateUrl: './statistics-panel.component.html',
  styleUrl: './statistics-panel.component.scss'
})
export class StatisticsPanelComponent implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  dashboard: Dashboard | null = null;
  refreshing = false;
  message = '';

  ngOnInit(): void { this.refresh(); }

  refresh(): void {
    this.refreshing = true;
    this.message = '';
    this.dashboardService.getDashboard().pipe(finalize(() => this.refreshing = false)).subscribe({
      next: dashboard => this.dashboard = dashboard,
      error: () => this.message = 'Não foi possível carregar as estatísticas.'
    });
  }
}
