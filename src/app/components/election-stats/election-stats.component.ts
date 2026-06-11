import { CommonModule } from '@angular/common';
import { Component, inject, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ElectionStatsAPI } from 'src/app/services/electionStatsAPI.service';

@Component({
  selector: 'app-election-stats',
  templateUrl: './election-stats.component.html',
  styleUrls: ['./election-stats.component.css'],
  standalone: true,
  imports: [CommonModule]
})
export class ElectionStatsComponent implements OnInit {
  route: ActivatedRoute = inject(ActivatedRoute);
  electionStatsAPI: ElectionStatsAPI = inject(ElectionStatsAPI)
  electionID: any;
  govIDs: any;

  // ─── UI State ─────────────────────────────────────
  activeTab: string = 'overall';
  selectedGovIndex: number = 0;
  selectedConstGovIndex: number = 0;

  // ─── Data Variables (wire your API responses to these) ───
  overallData: any = null;
  governorateResults: any[] = [];
  participationData: any = null;
  constituencyData: any = null;
  demographicData: any = null;

  // ─── Chart Config ─────────────────────────────────
  candidateColors: string[] = [
    '#224abe', '#10b981', '#f59e0b', '#ef4444',
    '#8b5cf6', '#ec4899', '#14b8a6', '#f97316'
  ];
  imageBaseUrl: string = '';

  ngOnInit() {
    this.electionID = this.route.snapshot.params['id'];

    this.electionStatsAPI.getElectionStats(this.electionID).subscribe({
      next: (res: any) => {
        console.log("Overall: ", res);
        this.overallData = res;
      },
      error: (err) => {
        console.log(err);
      }
    })

    this.electionStatsAPI.getElectionStatsByGov(this.electionID).subscribe({
      next: (res: any) => {
        console.log("By Gov: ", res);
        this.governorateResults = res.governorates as any[];
        this.govIDs = res.governorates.map((gov: any) => {
          return gov.governorateId;
        })
        console.log(this.govIDs)
        if (this.govIDs && this.govIDs.length > 0) {
          this.loadConstituencyData(0);
        }
      },
      error: (err) => {
        console.log(err);
      }
    })



    this.electionStatsAPI.getParticipationRate(this.electionID).subscribe({
      next: (res) => {
        console.log("Participation Rate: ", res);
        this.participationData = res;
      },
      error: (err) => {
        console.log(err);
      }
    })

    this.electionStatsAPI.getDemographicStats(this.electionID).subscribe({
      next: (res) => {
        console.log("Demographics: ", res);
        this.demographicData = res;
      },
      error: (err) => {
        console.log(err);
      }
    })
  }

  /** Smart photo URL builder - handles full URLs (Cloudinary) and relative paths */
  getPhotoUrl(photoUrl: string): string {
    if (!photoUrl) return '';
    return photoUrl.startsWith('http') ? photoUrl : this.imageBaseUrl + photoUrl;
  }

  /** Get gender label in Arabic */
  getGenderLabel(gender: string): string {
    if (gender === 'Male' || gender === 'ذكر') return 'ذكور';
    if (gender === 'Female' || gender === 'أنثى') return 'إناث';
    return gender;
  }

  /** Get age group label in Arabic */
  getAgeLabel(ageGroup: string): string {
    const labels: any = {
      '18-25': '١٨-٢٥ سنة',
      '26-35': '٢٦-٣٥ سنة',
      '36-45': '٣٦-٤٥ سنة',
      '46-60': '٤٦-٦٠ سنة',
      '60+': '٦٠+ سنة'
    };
    return labels[ageGroup] || ageGroup;
  }

  /** Calculate SVG donut chart offset for each candidate segment */
  getCumulativeOffset(index: number): number {
    if (!this.overallData?.candidateResults) return 25;
    let offset = 25; // Start from 12 o'clock position
    for (let i = 0; i < index; i++) {
      offset -= this.overallData.candidateResults[i].percentage;
    }
    return offset;
  }

  /**
   * Load constituency data for a given governorate.
   * Called when user clicks a governorate pill in the constituency tab.
   * TODO: wire this to your API service
   */
  loadConstituencyData(govIndex: number): void {
    if (!this.govIDs || this.govIDs.length <= govIndex) return;
    this.selectedConstGovIndex = govIndex;
    const govId = this.govIDs[govIndex];
    this.electionStatsAPI.getElectionStatsByConstituency(this.electionID, govId).subscribe({
      next: (res: any) => {
        console.log("By Constituency: ", res);
        this.constituencyData = res;
      },
      error: (err) => console.log(err)
    });
  }
}
