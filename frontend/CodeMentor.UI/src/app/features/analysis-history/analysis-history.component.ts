import { Component } from '@angular/core';
import { PageHeadingComponent } from '../../shared/components/page-heading.component';
import { FeatureUnavailableComponent } from '../../shared/components/feature-unavailable.component';

@Component({
  imports: [PageHeadingComponent, FeatureUnavailableComponent],
  templateUrl: './analysis-history.component.html',
  styleUrl: './analysis-history.component.scss',
})
export class AnalysisHistoryComponent {}
