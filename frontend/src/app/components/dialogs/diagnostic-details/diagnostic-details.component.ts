import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';

export interface DiagnosticItem {
  title: string;
  meta: string;
  details: string;
}

export interface DiagnosticDetailsData {
  title: string;
  items: DiagnosticItem[];
}

@Component({
  selector: 'app-diagnostic-details',
  imports: [MatDialogModule],
  templateUrl: './diagnostic-details.component.html',
  styleUrl: './diagnostic-details.component.scss'
})
export class DiagnosticDetailsComponent {
  readonly data = inject<DiagnosticDetailsData>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<DiagnosticDetailsComponent>);

  close(): void { this.dialogRef.close(); }
}
