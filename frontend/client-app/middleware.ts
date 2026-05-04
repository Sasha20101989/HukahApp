import { NextResponse, type NextRequest } from "next/server";

export function middleware(request: NextRequest) {
  const hasSession = request.cookies.has("hookah_client_access_token");
  if (!hasSession) {
    const url = request.nextUrl.clone();
    url.pathname = "/";
    url.searchParams.set("login", "required");
    return NextResponse.redirect(url);
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/account/:path*"]
};
