import { Component, input } from '@angular/core';

@Component({
  selector: 'app-page-heading',
  template: `
    <div class="page-heading">
      <div>
        @if (eyebrow()) {
          <div class="eyebrow">{{ eyebrow() }}</div>
        }
        <h1>{{ title() }}</h1>
        @if (description()) {
          <p>{{ description() }}</p>
        }
      </div>
      <div class="heading-actions"><ng-content /></div>
    </div>
  `,
  styles: `
    .page-heading { display: flex; align-items: flex-end; justify-content: space-between; gap: 24px; margin-bottom: 28px; }
    .eyebrow { color: #7772db; font-size: 11px; font-weight: 750; letter-spacing: .12em; margin-bottom: 8px; text-transform: uppercase; }
    h1 { color: #19213b; font-size: clamp(26px, 3vw, 34px); letter-spacing: -.04em; line-height: 1.15; margin: 0; }
    p { color: #737b91; font-size: 14px; line-height: 1.65; margin: 9px 0 0; max-width: 650px; }
    .heading-actions { display: flex; flex-wrap: wrap; gap: 10px; }
    @media (max-width: 680px) { .page-heading { align-items: flex-start; flex-direction: column; } }
  `,
})
export class PageHeadingComponent {
  readonly title = input.required<string>();
  readonly description = input('');
  readonly eyebrow = input('');
}
