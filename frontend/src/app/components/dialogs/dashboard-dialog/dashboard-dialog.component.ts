import { Component, inject } from '@angular/core';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { DashboardTab } from '../../../models';
import { FeedManagementComponent } from '../../feeds/feed-management/feed-management.component';
import { StatisticsPanelComponent } from '../../statistics/statistics-panel/statistics-panel.component';
import { TelemetryPanelComponent } from '../../telemetry/telemetry-panel/telemetry-panel.component';
import { SystemPanelComponent } from '../../system/system-panel/system-panel.component';

@Component({
  selector: 'app-dashboard-dialog',
  imports: [MatDialogModule, StatisticsPanelComponent, FeedManagementComponent, TelemetryPanelComponent, SystemPanelComponent],
  templateUrl: './dashboard-dialog.component.html',
  styleUrl: './dashboard-dialog.component.scss'
})
export class DashboardDialogComponent {
  private readonly dialogRef = inject(MatDialogRef<DashboardDialogComponent>);

  tab: DashboardTab = 'stats';

  close(): void {
    this.dialogRef.close({ refresh: true });
  }

}
