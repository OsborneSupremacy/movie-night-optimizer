global using Amazon.DynamoDBv2;
global using Amazon.DynamoDBv2.Model;

global using Amazon.Lambda.APIGatewayEvents;
global using Amazon.Lambda.Core;

global using dotenv.net.Utilities;

global using FluentValidation;

global using JetBrains.Annotations;

global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;

global using System.Collections.Immutable;
global using System.Net;
global using System.Text;
global using System.Text.Json;

global using MovieNight.Library.Abstractions;
global using MovieNight.Library.Builders;
global using MovieNight.Library.Extensions;
global using MovieNight.Library.Messaging;
global using MovieNight.Library.Models;
global using MovieNight.Library.Providers;
global using MovieNight.Library.Services;
global using MovieNight.Library.Utility;
global using MovieNight.Library.Validators;
