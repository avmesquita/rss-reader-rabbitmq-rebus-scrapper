import { Component, inject, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { finalize } from 'rxjs';
import { ApiError, DebugStatus, QueueMessage, RabbitQueue } from '../../../models';
import { DashboardService } from '../../../services/dashboard.service';
import { DiagnosticDetailsComponent, DiagnosticItem } from '../../dialogs/diagnostic-details/diagnostic-details.component';

@Component({
  selector: 'app-telemetry-panel',
  templateUrl: './telemetry-panel.component.html',
  styleUrl: './telemetry-panel.component.scss',
  imports: [MatButtonModule, MatDialogModule]
})
export class TelemetryPanelComponent implements OnInit {
  private readonly dashboardService = inject(DashboardService);
  private readonly dialog = inject(MatDialog);

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

  openQueue(queue: RabbitQueue): void {
    this.refreshing = true;
    this.message = '';
    this.dashboardService.getQueueMessages(queue.name).pipe(finalize(() => this.refreshing = false)).subscribe({
      next: messages => this.openDetails(`Fila ${queue.name}`, messages.map((item, index) => this.queueItem(item, index))),
      error: () => this.message = `Não foi possível consultar as mensagens da fila "${queue.name}".`
    });
  }

  openApiErrors(errors: ApiError[]): void {
    this.openDetails('Erros recentes da API', errors.map(error => ({
      title: `${error.method} ${error.path}`,
      meta: new Date(error.occurredAt).toLocaleString('pt-BR'),
      details: error.message
    })));
  }

  private openDetails(title: string, items: DiagnosticItem[]): void {
    this.dialog.open(DiagnosticDetailsComponent, { data: { title, items }, width: '820px', maxWidth: '96vw', maxHeight: '90vh' });
  }

  private queueItem(message: QueueMessage, index: number): DiagnosticItem {
    return {
      title: `Mensagem ${index + 1}`,
      meta: `${message.exchange || '(sem exchange)'} → ${message.routing_key || '(sem routing key)'} · ${message.payload_bytes} bytes${message.redelivered ? ' · reenviada' : ''}`,
      details: `${message.payload ?? ''}\n\nPropriedades:\n${JSON.stringify(message.properties ?? {}, null, 2)}`
    };
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
