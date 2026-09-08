/** @type {import('jest').Config} */
module.exports = {
  preset: "ts-jest",
  testEnvironment: "node",
  roots: ["<rootDir>/tests"],
  testMatch: ["**/*.test.ts"],
  collectCoverageFrom: [
    "handlers/**/*.ts",
    "services/**/*.ts",
    "domain/**/*.ts",
    "policy/**/*.ts",
    "adapters/**/*.ts",
    "shared/**/*.ts",
    "config/**/*.ts",
    "!**/*.d.ts",
  ],
  coverageDirectory: "coverage",
};
