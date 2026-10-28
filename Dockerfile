FROM rust:1-bookworm AS core
WORKDIR /build
COPY core/Cargo.toml core/Cargo.lock ./
COPY core/src ./src
RUN cargo build --locked --release

FROM node:22-bookworm-slim
ENV NODE_ENV=production HOST=0.0.0.0 PORT=3000 CORE_BINARY=/app/bin/stremio-vr-core
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --omit=dev && npm cache clean --force
COPY --from=core /build/target/release/stremio-vr-core /app/bin/stremio-vr-core
COPY server ./server
COPY LICENSE ./LICENSE
USER node
EXPOSE 3000
HEALTHCHECK --interval=30s --timeout=5s CMD node -e "fetch('http://127.0.0.1:'+process.env.PORT+'/health').then(r=>{if(!r.ok)process.exit(1)}).catch(()=>process.exit(1))"
CMD ["node", "server/index.js"]
