import { Component, inject, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { finalize } from 'rxjs';
import { DebugStatus } from '../../../models';
import { DashboardService } from '../../../services/dashboard.service';

@Component({
  selector: 'app-telemetry-panel',
  templateUrl: './telemetry-panel.component.html',
  styleUrl: './telemetry-panel.component.scss',
  imports: [
    MatButtonModule
  ]
})
export class TelemetryPanelComponent implements OnInit {
  private readonly dashboardService = inject(DashboardService);

  debug: DebugStatus | null = null;
  refreshing = false;
  message = '';

  ngOnInit(): void {
    this.loadTelemetry();
  }

  refresh(): void {
    this.refreshing = true;
    this.message = '';
    this.loadTelemetry();
  }

  private loadTelemetry(): void {
    this.dashboardService.getDebugStatus().pipe(
      finalize(() => this.refreshing = false)
    ).subscribe({
      next: debug => this.debug = debug,
      error: () => this.message = 'Não foi possível carregar a telemetria.'
    });
  }
}
