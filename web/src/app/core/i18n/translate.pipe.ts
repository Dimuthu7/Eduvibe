import { inject, Pipe, PipeTransform } from '@angular/core';
import { TranslateService } from './translate.service';

/** {{ 'login.title' | t }} — impure on purpose so text updates when the language changes. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(TranslateService);

  transform(key: string): string {
    return this.i18n.translate(key);
  }
}
