/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  images: {
    qualities: [75, 95],
  },
  serverExternalPackages: ['gamedig'],
};

export default nextConfig;
