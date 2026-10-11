'use strict';

const MAX_NESTING = 64;

const assertDepth = (depth, limit = MAX_NESTING + 1) => {
  if (depth > limit) {
    throw new SyntaxError(`Brace pattern nesting exceeds maximum depth (${MAX_NESTING})`);
  }
};

module.exports = { MAX_NESTING, assertDepth };
