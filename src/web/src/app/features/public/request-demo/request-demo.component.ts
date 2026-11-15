import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslocoModule } from '@jsverse/transloco';
import { countryOptions } from '../../../core/demo/countries';
import { DEMO_INDUSTRIES, DEMO_ORGANIZATION_SIZES, DemoRequestForm } from '../../../core/demo/demo-requests.models';
import { DemoRequestsService } from '../../../core/demo/demo-requests.service';
import { LanguageService } from '../../../core/i18n/language.service';
import { BrandLogoComponent } from '../../../shared/ui/brand-logo.component';
import { LanguageSwitcherComponent } from '../../../shared/ui/language-switcher.component';

/**
 * "Request a demo" (P13.52): the public form of the landing page. Five things are required (name, work e-mail,
 * organization, country, consent to be contacted); the rest helps prepare the demo. The answer is the same thank-you
 * whether the request is new or the same address already sent one. A hidden field catches robots.
 */
@Component({
  selector: 'app-request-demo',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule, MatSelectModule, RouterLink, TranslocoModule, BrandLogoComponent, LanguageSwitcherComponent],
  templateUrl: './request-demo.component.html',
  styleUrls: ['../login/login.component.scss', './request-demo.component.scss'],
})
export class RequestDemoComponent {
  private readonly demoRequests = inject(DemoRequestsService);
  private readonly fb = inject(FormBuilder);
  readonly language = inject(LanguageService);

  readonly industries = DEMO_INDUSTRIES;
  readonly sizes = DEMO_ORGANIZATION_SIZES;
  /** Country names come from the browser in the page's language, sorted by that name. */
  readonly countries = computed(() => countryOptions(this.language.current()));

  readonly form = this.fb.nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(150)]],
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    organizationName: ['', [Validators.required, Validators.maxLength(200)]],
    countryCode: ['', [Validators.required]],
    jobTitle: ['', [Validators.maxLength(150)]],
    phone: ['', [Validators.maxLength(40), Validators.pattern(/^[0-9+ ()-]*$/)]],
    industry: [''],
    organizationSize: [''],
    databaseSystems: ['', [Validators.maxLength(200)]],
    demoLanguage: [this.language.current() as string],
    preferredTime: ['', [Validators.maxLength(200)]],
    message: ['', [Validators.maxLength(2000)]],
    consent: [false, [Validators.requiredTrue]],
    website: [''],
  });
  readonly busy = signal(false);
  readonly sent = signal(false);
  readonly error = signal<'rate_limited' | 'invalid' | 'unknown' | null>(null);

  async submit(): Promise<void> {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set(null);
    const value = this.form.getRawValue();
    const optional = (text: string): string | undefined => text.trim() || undefined;
    const request: DemoRequestForm = {
      fullName: value.fullName.trim(),
      email: value.email.trim(),
      organizationName: value.organizationName.trim(),
      countryCode: value.countryCode,
      consent: value.consent,
      jobTitle: optional(value.jobTitle),
      phone: optional(value.phone),
      industry: optional(value.industry),
      organizationSize: optional(value.organizationSize),
      databaseSystems: optional(value.databaseSystems),
      demoLanguage: value.demoLanguage,
      preferredTime: optional(value.preferredTime),
      message: optional(value.message),
      pageLanguage: this.language.current(),
      website: optional(value.website),
    };
    try {
      await this.demoRequests.submit(request);
      this.sent.set(true);
    } catch (failure) {
      const response = failure instanceof HttpErrorResponse ? failure : null;
      if (response?.status === 429) {
        this.error.set('rate_limited');
      } else if (response?.status === 400 && this.showFieldErrors(response)) {
        this.error.set('invalid');
      } else {
        this.error.set('unknown');
      }
    } finally {
      this.busy.set(false);
    }
  }

  /** Marks the fields the API refused; false when the answer names none of this form's fields. */
  private showFieldErrors(response: HttpErrorResponse): boolean {
    const errors = (response.error as { errors?: Record<string, string[]> } | null)?.errors ?? {};
    let found = false;
    for (const [field, codes] of Object.entries(errors)) {
      const control = this.form.get(field);
      if (control) {
        control.setErrors({ server: codes[0] ?? 'invalid' });
        control.markAsTouched();
        found = true;
      }
    }
    return found;
  }
}
