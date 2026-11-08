import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from '../../core/i18n/language.service';
import { DateFormat, formatDateValue, formatNumberValue } from './locale-format';

/** `value | wiDate: 'medium'`: a date in the interface language (replaces DatePipe, which follows the build locale only). */
@Pipe({ name: 'wiDate', pure: false })
export class WiDatePipe implements PipeTransform {
  private readonly language = inject(LanguageService);

  transform(value: string | number | Date | null | undefined, format: DateFormat = 'mediumDate'): string {
    return formatDateValue(value, format, this.language.current());
  }
}

/** `value | wiNumber: '1.0-2'`: a number in the interface language (replaces DecimalPipe). */
@Pipe({ name: 'wiNumber', pure: false })
export class WiNumberPipe implements PipeTransform {
  private readonly language = inject(LanguageService);

  transform(value: number | string | null | undefined, digitsInfo?: string): string {
    return formatNumberValue(value, digitsInfo, this.language.current());
  }
}
