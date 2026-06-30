// Shared whitelist application status definitions — used by the dashboard,
// the application form, and the admin review page so everything stays in sync.

export const WL_STATUS = {
  pending: {
    label: "Pending Review",
    short: "PENDING",
    color: "#ffb020",
    desc: "Your application is in the queue and being reviewed by staff.",
  },
  approved: {
    label: "Approved",
    short: "APPROVED",
    color: "#2bff88",
    desc: "You're whitelisted! Hop in and play on PROJECT RIFT.",
  },
  rejected: {
    label: "Rejected",
    short: "REJECTED",
    color: "#ff5470",
    desc: "Your application was not approved. See the staff note for details.",
  },
  resubmit: {
    label: "Re-fill Required",
    short: "RE-FILL",
    color: "#00e5ff",
    desc: "Staff need more / corrected info. Please review the note and submit an updated application.",
  },
  suspended: {
    label: "Suspended",
    short: "SUSPENDED",
    color: "#ff8a3d",
    desc: "Your whitelist access is currently suspended. Contact staff on Discord.",
  },
  out_of_region: {
    label: "Out of Server Region",
    short: "OUT OF REGION",
    color: "#b388ff",
    desc:
      "You appear to be outside the Singapore server region. Your ping is too high for fair gameplay, so you can't be whitelisted from this location.",
  },
};

// statuses an admin can assign from the review page
export const ADMIN_STATUSES = [
  "approved",
  "rejected",
  "resubmit",
  "suspended",
  "out_of_region",
  "pending",
];

export function statusMeta(key) {
  return WL_STATUS[key] || { label: key || "Unknown", short: (key || "—").toUpperCase(), color: "#9aa3c4", desc: "" };
}
