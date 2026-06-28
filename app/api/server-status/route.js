import { NextResponse } from 'next/server';
import { getLiveRustStatus } from '@/lib/data';

export const revalidate = 60; // Cache for 60 seconds

export async function GET() {
  const liveStatus = await getLiveRustStatus();
  
  if (!liveStatus) {
    return NextResponse.json({ status: "OFFLINE", error: "Missing server configuration" }, { status: 500 });
  }

  if (liveStatus.status === "OFFLINE") {
    return NextResponse.json(liveStatus, { status: 200 });
  }

  return NextResponse.json(liveStatus);
}
