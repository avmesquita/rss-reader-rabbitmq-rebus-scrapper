import { Component, inject, OnInit } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { Dashboard, DashboardTab } from '../../../models';
import { FeedManagementComponent } from '../../feeds/feed-management/feed-management.component';
import { DashboardService } from '../../../services/dashboard.service';
import { TelemetryPanelComponent } from '../../telemetry/telemetry-panel/telemetry-panel.component';
import { SystemPanelComponent } from '../../system/system-panel/system-panel.component';

@Component({
  selector: 'app-dashboard-dialog',
  imports: [MatDialogModule, FeedManagementComponent, TelemetryPanelComponent, SystemPanelComponent],
  templateUrl: './dashboard-dialog.component.html',
  styleUrl: './dashboard-dialog.component.scss'
})
export class DashboardDialogComponent implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  private readonly dialogRef = inject(MatDialogRef<DashboardDialogComponent>);

  dashboard: Dashboard | null = null;
  tab: DashboardTab = 'stats';

  ngOnInit(): void {
    this.loadDashboard();
  }

  close(): void {
    this.dialogRef.close({ refresh: true });
  }

  loadDashboard(): void {
    this.dashboardService.getDashboard().subscribe({
      next: dashboard => this.dashboard = dashboard
    });
  }

}
