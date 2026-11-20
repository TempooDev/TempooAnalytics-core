import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { FeedbackService } from '@app/shared/services/feedback.service';
import { ToastService } from '@app/shared/services/toast.service';
import { APP_SETTINGS } from '@app/core/app-settings';

@Component({
  selector: 'app-feedback-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './feedback.page.html',
})
export class FeedbackPage {
  private feedbackService = inject(FeedbackService);
  private toast = inject(ToastService);
  private router = inject(Router);

  /** Empty when the operator set no contact address: the contact line is hidden. */
  supportEmail = inject(APP_SETTINGS).supportEmail;

  title = signal('');
  description = signal('');
  isSubmitting = signal(false);

  async submitFeedback() {
    if (!this.title() || !this.description()) {
      this.toast.error('Please fill in all fields');
      return;
    }

    this.isSubmitting.set(true);
    try {
      await this.feedbackService.sendFeedback(this.title(), this.description()).toPromise();
      this.toast.success('Thank you for your feedback!');
      this.title.set('');
      this.description.set('');
    } catch (error) {
      this.toast.error('Failed to send feedback. Please try again later.');
      console.error('Feedback error', error);
    } finally {
      this.isSubmitting.set(false);
    }
  }
}
