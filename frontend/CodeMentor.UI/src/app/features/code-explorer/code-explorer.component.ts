import { Component } from '@angular/core';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { FeatureUnavailableComponent } from '../../shared/components/feature-unavailable.component';

@Component({
  imports: [PageHeadingComponent, FeatureUnavailableComponent],
  templateUrl: './code-explorer.component.html',
  styleUrl: './code-explorer.component.scss',
})
export class CodeExplorerComponent {}
