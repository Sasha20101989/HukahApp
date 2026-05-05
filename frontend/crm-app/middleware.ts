import { NextResponse, type NextRequest } from "next/server";

const authCookie = "hookah_crm_access_token";

export function middleware(request: NextRequest) {
  const isLogin = request.nextUrl.pathname === "/login";
  const hasSession = Boolean(request.cookies.get(authCookie)?.value);

  if (!hasSession && !isLogin) {
    return NextResponse.redirect(new URL("/login", request.url));
  }

  if (hasSession && isLogin) {
    return NextResponse.redirect(new URL("/", request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/", "/login", "/admin/:path*"]
};
