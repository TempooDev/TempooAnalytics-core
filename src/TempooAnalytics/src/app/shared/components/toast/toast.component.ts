import { Component, Input, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';

export type ToastType = 'success' | 'error' | 'warning' | 'info';

@Component({
    standalone: true,
    selector: 'app-toast',
    imports: [CommonModule],
    template: `
    <div class="toast-body" [ngClass]="type">
      <!-- Success Icon -->
      @if (type === 'success') {
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="toast-icon"><polyline points="20 6 9 17 4 12"></polyline></svg>
      }
      <!-- Error Icon -->
      @if (type === 'error') {
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="toast-icon"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>
      }
      <!-- Warning Icon -->
      @if (type === 'warning') {
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="toast-icon"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>
      }
      <!-- Info Icon -->
      @if (type === 'info') {
        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" class="toast-icon"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>
      }
      
      <span class="message">{{ message }}</span>
    </div>
  `,
    styles: [
        `:host { position: fixed; bottom: 32px; right: 32px; z-index: 11000; pointer-events: none; display: block; }`,
        `.toast-body { 
            pointer-events: auto; 
            background: rgba(15, 23, 42, 0.95); 
            backdrop-filter: blur(12px);
            color: #f8fafc; 
            padding: 14px 20px; 
            border-radius: 16px; 
            border: 1px solid rgba(255, 255, 255, 0.1);
            box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.5), 0 10px 10px -5px rgba(0, 0, 0, 0.4);
            display: flex;
            align-items: center;
            gap: 12px;
            font-size: 14px;
            font-weight: 600;
            opacity: 0; 
            transform: translateY(20px);
            transition: all 400ms cubic-bezier(0.23, 1, 0.32, 1);
        }`,
        `.message { line-height: 1.4; }`,
        
        /* Success Styles */
        `.success { border-color: rgba(16, 185, 129, 0.3); }`,
        `.success .toast-icon { color: #10b981; }`,
        
        /* Error Styles */
        `.error { border-color: rgba(244, 63, 94, 0.3); }`,
        `.error .toast-icon { color: #f43f5e; }`,
        
        /* Warning Styles */
        `.warning { border-color: rgba(245, 158, 11, 0.3); }`,
        `.warning .toast-icon { color: #f59e0b; }`,
        
        /* Info Styles */
        `.info { border-color: rgba(56, 189, 248, 0.3); }`,
        `.info .toast-icon { color: #38bdf8; }`
    ],
    changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ToastComponent {
    @Input() message = '';
    @Input() type: ToastType = 'info';
}
