(function () {
    'use strict';

    angular.module('MedTrialsMcpApp').factory('mcpClient', ['$http', '$q', '$timeout', '$rootScope', function ($http, $q, $timeout, $rootScope) {
        var defaultRoute = '/mcp';
        var baseMeta = document.querySelector('meta[name="mcp-base-route"]');
        var baseRoute = (baseMeta && baseMeta.getAttribute('content')) ? baseMeta.getAttribute('content') : defaultRoute;
        if (!baseRoute || typeof baseRoute !== 'string') {
            baseRoute = defaultRoute;
        }
        baseRoute = baseRoute.trim();
        if (!baseRoute.startsWith('/')) {
            baseRoute = '/' + baseRoute;
        }
        if (baseRoute.length > 1 && baseRoute.endsWith('/')) {
            baseRoute = baseRoute.slice(0, -1);
        }

        function makeUrl(segment) {
            if (!segment) {
                return baseRoute;
            }
            if (!segment.startsWith('/')) {
                segment = '/' + segment;
            }
            return baseRoute + segment;
        }

        var eventSource = null;
        var sessionId = null;
        var connectionState = 'disconnected';
        var reconnectTimer = null;
        var reconnectAttempt = 0;
        var waiters = [];
        var pending = {};
        var requestCounter = 0;
        var cachedCapabilities = null;
        var capabilitiesPromise = null;
        var cachedTools = null;

        var status = {
            state: 'disconnected',
            sessionId: null,
            lastHeartbeat: null,
            lastMessageAt: null,
            connectedAt: null,
            lastError: null,
            reconnecting: false,
            pendingRequests: 0,
            transport: 'http+sse',
            streamEndpoint: makeUrl('sse'),
            rpcEndpoint: makeUrl('rpc'),
            capabilityEndpoint: makeUrl('capabilities')
        };

        function applyStatus(mutator) {
            $rootScope.$evalAsync(function () {
                if (typeof mutator === 'function') {
                    mutator(status);
                }
                status.sessionId = sessionId;
                status.state = connectionState;
                status.pendingRequests = Object.keys(pending).length;
            });
        }

        function safeParse(json) {
            if (!json) {
                return null;
            }
            try {
                return JSON.parse(json);
            } catch (err) {
                console.warn('Failed to parse MCP payload', err);
                return null;
            }
        }

        function resolveWaiters(value) {
            if (waiters.length === 0) {
                return;
            }
            waiters.forEach(function (deferred) {
                deferred.resolve(value);
            });
            waiters = [];
        }

        function rejectWaiters(reason) {
            if (waiters.length === 0) {
                return;
            }
            waiters.forEach(function (deferred) {
                deferred.reject(reason);
            });
            waiters = [];
        }

        function cleanupPending(id) {
            var entry = pending[id];
            if (!entry) {
                return;
            }
            if (entry.timeout) {
                $timeout.cancel(entry.timeout);
            }
            delete pending[id];
            applyStatus();
        }

        function rejectPending(reason) {
            Object.keys(pending).forEach(function (id) {
                var entry = pending[id];
                if (!entry) {
                    return;
                }
                entry.deferred.reject({
                    code: 'connection_lost',
                    message: reason || 'MCP connection lost before the operation completed.'
                });
                cleanupPending(id);
            });
        }

        function ensureConnected() {
            if (connectionState === 'connected' && sessionId) {
                return $q.resolve(sessionId);
            }

            var deferred = $q.defer();
            waiters.push(deferred);

            if (connectionState === 'disconnected') {
                connect();
            }

            return deferred.promise;
        }

        function scheduleReconnect() {
            if (reconnectTimer) {
                return;
            }

            reconnectAttempt += 1;
            var delay = Math.min(15000, Math.pow(2, reconnectAttempt) * 1000);
            applyStatus(function (s) {
                s.reconnecting = true;
            });

            reconnectTimer = $timeout(function () {
                reconnectTimer = null;
                connect();
            }, delay);
        }

        function handleJsonRpc(message) {
            if (!message || typeof message !== 'object') {
                return;
            }

            var id = Object.prototype.hasOwnProperty.call(message, 'id') ? message.id : null;
            applyStatus(function (s) {
                s.lastMessageAt = new Date();
            });

            if (id === null || id === undefined) {
                return;
            }

            var entry = pending[id];
            if (!entry) {
                return;
            }

            if (Object.prototype.hasOwnProperty.call(message, 'result')) {
                entry.deferred.resolve(message.result);
            } else if (Object.prototype.hasOwnProperty.call(message, 'error')) {
                var err = message.error || {};
                if (typeof err === 'string') {
                    err = { code: 'error', message: err };
                }
                entry.deferred.reject(err);
            } else {
                entry.deferred.resolve(message);
            }

            cleanupPending(id);
        }

        function handleAck(event) {
            var payload = safeParse(event.data);
            if (!payload || !payload.id) {
                return;
            }
            var entry = pending[payload.id];
            if (entry) {
                entry.acknowledged = new Date();
            }
        }

        function handleHeartbeat(event) {
            var payload = safeParse(event.data);
            var timestamp = payload && payload.timestamp ? new Date(payload.timestamp) : new Date();
            applyStatus(function (s) {
                s.lastHeartbeat = timestamp;
            });
        }

        function handleConnected(event) {
            reconnectAttempt = 0;
            var payload = safeParse(event.data) || {};
            sessionId = payload.sessionId || null;
            connectionState = sessionId ? 'connected' : 'connecting';
            applyStatus(function (s) {
                var connectedAt = payload.timestamp ? new Date(payload.timestamp) : new Date();
                s.connectedAt = connectedAt;
                s.lastHeartbeat = connectedAt;
                s.reconnecting = false;
                s.lastError = null;
            });
            if (sessionId) {
                resolveWaiters(sessionId);
            }
        }

        function handleServerError(event) {
            if (event && typeof event.data === 'string' && event.data.length > 0) {
                var payload = safeParse(event.data);
                if (payload) {
                    handleJsonRpc(payload);
                }
                return;
            }
            handleDisconnect('Server reported an error.');
        }

        function handleDisconnect(reason) {
            if (eventSource) {
                try { eventSource.close(); } catch (err) { /* ignore */ }
                eventSource = null;
            }

            if (connectionState !== 'disconnected') {
                connectionState = 'disconnected';
                sessionId = null;
                applyStatus(function (s) {
                    s.lastError = reason || 'Connection closed.';
                });
            }

            rejectPending(reason);
            rejectWaiters(reason || 'Connection closed.');
            scheduleReconnect();
        }

        function connect() {
            if (eventSource) {
                try { eventSource.close(); } catch (err) { /* ignore */ }
                eventSource = null;
            }
            if (reconnectTimer) {
                $timeout.cancel(reconnectTimer);
                reconnectTimer = null;
            }

            connectionState = 'connecting';
            applyStatus(function (s) {
                s.reconnecting = false;
            });

            try {
                eventSource = new EventSource(makeUrl('sse'), { withCredentials: true });
            } catch (err) {
                connectionState = 'disconnected';
                applyStatus(function (s) {
                    s.lastError = err && err.message ? err.message : 'Failed to open SSE stream.';
                });
                scheduleReconnect();
                return;
            }

            eventSource.addEventListener('connected', handleConnected);
            eventSource.addEventListener('heartbeat', handleHeartbeat);
            eventSource.addEventListener('ack', handleAck);
            eventSource.addEventListener('result', function (event) {
                var payload = safeParse(event.data);
                if (payload) {
                    handleJsonRpc(payload);
                }
            });
            eventSource.addEventListener('error', handleServerError);
            eventSource.addEventListener('end', function (event) {
                handleDisconnect('Stream completed.');
            });
            eventSource.onerror = function (event) {
                if (event && typeof event.data === 'string' && event.data.length > 0) {
                    handleServerError(event);
                    return;
                }
                handleDisconnect('Connection error.');
            };
        }

        function sendRpc(method, params) {
            return ensureConnected().then(function () {
                var id = (++requestCounter).toString();
                var deferred = $q.defer();
                var timeout = $timeout(function () {
                    if (pending[id]) {
                        pending[id].deferred.reject({
                            code: 'timeout',
                            message: 'MCP request timed out.'
                        });
                        cleanupPending(id);
                    }
                }, 30000);

                pending[id] = {
                    deferred: deferred,
                    timeout: timeout
                };
                applyStatus();

                var envelope = {
                    jsonrpc: '2.0',
                    id: id,
                    method: method
                };

                if (params !== undefined) {
                    envelope.params = params;
                }

                $http.post(makeUrl('rpc'), envelope, {
                    headers: {
                        'Content-Type': 'application/json',
                        'X-Mcp-Session': sessionId || ''
                    }
                }).catch(function (error) {
                    cleanupPending(id);
                    var payload = error && error.data ? error.data : { code: 'transport_error', message: 'Failed to submit MCP request.' };
                    deferred.reject(payload);
                });

                return deferred.promise;
            });
        }

        function normaliseCapabilities(data) {
            var transports = [];
            var tools = [];

            if (data && Array.isArray(data.transports)) {
                transports = data.transports.map(function (transport) {
                    return {
                        kind: transport.kind || transport.Kind || 'http+sse',
                        streamEndpoint: transport.streamEndpoint || transport.StreamEndpoint || makeUrl('sse'),
                        submitEndpoint: transport.submitEndpoint || transport.SubmitEndpoint || makeUrl('rpc'),
                        capabilityEndpoint: transport.capabilityEndpoint || transport.CapabilityEndpoint || null,
                        requireAuthentication: !!(transport.requireAuthentication || transport.RequireAuthentication)
                    };
                });
            }

            if (data && Array.isArray(data.tools)) {
                tools = data.tools.map(function (tool) {
                    return {
                        name: tool.name || tool.Name,
                        description: tool.description || tool.Description || '',
                        requireAuthentication: !!(tool.requireAuthentication || tool.RequireAuthentication),
                        enabledTransports: tool.enabledTransports || tool.EnabledTransports || null
                    };
                });
            }

            return {
                version: (data && (data.version || data.Version)) || '2.0',
                transports: transports,
                tools: tools
            };
        }

        function getCapabilities(forceRefresh) {
            if (!forceRefresh && cachedCapabilities) {
                return $q.resolve(cachedCapabilities);
            }

            if (!forceRefresh && capabilitiesPromise) {
                return capabilitiesPromise;
            }

            capabilitiesPromise = $http.get(makeUrl('capabilities')).then(function (response) {
                var document = normaliseCapabilities(response.data || {});
                cachedCapabilities = document;
                capabilitiesPromise = null;

                if (document.transports.length > 0) {
                    applyStatus(function (s) {
                        var primary = document.transports[0];
                        s.transport = primary.kind;
                        s.streamEndpoint = primary.streamEndpoint;
                        s.rpcEndpoint = primary.submitEndpoint;
                        s.capabilityEndpoint = primary.capabilityEndpoint || makeUrl('capabilities');
                    });
                }

                return document;
            }).catch(function (error) {
                capabilitiesPromise = null;
                return $q.reject(error && error.data ? error.data : error);
            });

            return capabilitiesPromise;
        }

        function formatTool(tool) {
            var metadata = tool.metadata || tool.Metadata || {};
            var schema = tool.input_schema || tool.inputSchema || tool.InputSchema || {};
            var requiredScopes = metadata.requiredScopes || metadata.RequiredScopes || [];
            if (!Array.isArray(requiredScopes)) {
                requiredScopes = [];
            }

            return {
                name: tool.name,
                description: tool.description || '',
                schema: schema,
                schemaJson: JSON.stringify(schema, null, 2),
                metadata: metadata,
                entity: metadata.entity || metadata.Entity || null,
                operation: metadata.operation || metadata.Operation || null,
                returnsCollection: !!(metadata.returnsCollection || metadata.ReturnsCollection),
                isMutation: !!(metadata.isMutation || metadata.IsMutation),
                requiredScopes: requiredScopes
            };
        }

        function listTools(forceRefresh) {
            if (!forceRefresh && cachedTools) {
                return $q.resolve(cachedTools);
            }

            return sendRpc('tools/list').then(function (result) {
                var list = (result && Array.isArray(result.tools)) ? result.tools : [];
                cachedTools = list.map(formatTool);
                return cachedTools;
            });
        }

        function normaliseCallResult(result) {
            if (!result || typeof result !== 'object') {
                return {
                    success: false,
                    result: null,
                    shortCircuit: null,
                    headers: {},
                    warnings: [],
                    diagnostics: {},
                    errorCode: 'invalid_response',
                    errorMessage: 'MCP call returned no payload.'
                };
            }

            return {
                success: !!result.success,
                result: result.result || null,
                shortCircuit: result.short_circuit || result.shortCircuit || null,
                headers: result.headers || {},
                warnings: Array.isArray(result.warnings) ? result.warnings : [],
                diagnostics: result.diagnostics || {},
                errorCode: result.error_code || result.errorCode || null,
                errorMessage: result.error_message || result.errorMessage || null
            };
        }

        function callTool(name, args) {
            if (!name) {
                return $q.reject({ code: 'invalid_request', message: 'Tool name is required.' });
            }

            var parameters = { name: name };
            if (args && typeof args === 'object' && Object.keys(args).length > 0) {
                parameters.arguments = args;
            }

            return sendRpc('tools/call', parameters).then(function (result) {
                return normaliseCallResult(result);
            });
        }

        // Start the SSE connection eagerly.
        connect();

        return {
            status: status,
            getCapabilities: getCapabilities,
            refreshCapabilities: function () { return getCapabilities(true); },
            listTools: listTools,
            refreshTools: function () { cachedTools = null; return listTools(true); },
            callTool: callTool,
            baseRoute: baseRoute
        };
    }]);
})();
