'use strict';

const { workerData } = require('node:worker_threads');

exports.default = task => ({
  task: task.id,
  site: workerData[1].params.site,
  workerId: process.__tinypool_state__.workerId,
});
