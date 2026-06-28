/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  images: {
    qualities: [75, 95],
  },
  experimental: {
    serverComponentsExternalPackages: ['gamedig'],
  },
};

export default nextConfig;
